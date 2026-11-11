using System;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using com.radiant.engine.core;
using com.radiant.engine.runtime;

namespace com.radiant.engine.bundle;

/// <summary>Samples process and GPU metrics and displays frame-performance history.</summary>
public class PerformanceMonitor : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.Overlay;

    // Dependencies
    private Process Process;
    private TimeSpan LastCpuTime;
    private long LastSampleTimestamp;
    private List<PerformanceCounter> GpuCounters;

    // Frame timing - zero-allocation circular buffer
    private const int FrameHistorySize = 64; // Power of 2 for fast modulo
    private const int FrameHistoryMask = FrameHistorySize - 1;
    private readonly float[] FrameTimeHistory = new float[FrameHistorySize];
    private int FrameHistoryIndex;
    private int FrameHistoryCount;
    private float FrameTimeSum;
    private float FrameTimeAverage;

    // FPS calculation
    private const float UpdateInterval = 1.00f;
    private float Fps;
    private int FrameCount;
    private float Elapsed;
    private float SmoothedDisplayFps; // Smoothed FPS for display (less flickery)

    // System metrics (volatile for cross-thread reads)
    private volatile float MemoryMB;
    private volatile float PeakMemoryMB;
    private volatile float CpuUsage;
    private int CpuCoreCount;
    private float CpuCoreCountInv;
    private volatile float GpuUsage = -1f;

    // Background sampling thread
    private Thread SamplerThread;
    private volatile bool SamplerRunning;
    private const int SampleIntervalMs = 500; // Sample every 500ms in background

    // Precomputed constants
    private const float BytesToMB = 1f / (1024f * 1024f);

    // Cached string builders - eliminates per-frame allocations
    private readonly StringBuilder FpsBuilder = new(32);
    private readonly StringBuilder FrameTimeBuilder = new(32);
    private readonly StringBuilder TargetBuilder = new(32);
    private readonly StringBuilder CpuBuilder = new(48);
    private readonly StringBuilder GpuBuilder = new(32);
    private readonly StringBuilder RamBuilder = new(32);
    private readonly StringBuilder PeakBuilder = new(32);

    /// <inheritdoc/>
    public override void Initialize()
    {
        Process = Process.GetCurrentProcess();
        CpuCoreCount = Environment.ProcessorCount;
        CpuCoreCountInv = 1f / CpuCoreCount;

        LastCpuTime = Process.TotalProcessorTime;
        LastSampleTimestamp = Stopwatch.GetTimestamp();
        if (OperatingSystem.IsWindows()) InitializeGpuCounters();
        StartSamplerThread();

        Inspector.CreateWindow("perf", "Profiling", 1);

        Inspector.AddSectionLabel("perf", "perfHeader", "Performance");
        Inspector.AddMetric("perf", "fps", "FPS: -");
        Inspector.AddMetric("perf", "frame", "Frame time: -");
        Inspector.AddMetric("perf", "target", "Target: -");
        Inspector.AddMetric("perf", "cpu", "CPU: -");
        Inspector.AddMetric("perf", "gpu", "GPU: -");

        Inspector.AddSectionLabel("perf", "memHeader", "Memory");
        Inspector.AddMetric("perf", "ram", "RAM: -");
        Inspector.AddMetric("perf", "peak", "Peak: -");
        Inspector.SetFrameHistory(FrameTimeHistory, 0, 0, 0);
    }

    /// <summary>Starts background collection of process and device metrics.</summary>
    private void StartSamplerThread()
    {
        SamplerRunning = true;
        SamplerThread = new Thread(SamplerLoop)
        {
            IsBackground = true,
            Name = "PerformanceMonitor Sampler",
            Priority = ThreadPriority.BelowNormal
        };
        SamplerThread.Start();
    }

    /// <summary>Collects performance samples until disposal requests shutdown.</summary>
    private void SamplerLoop()
    {
        while (SamplerRunning)
        {
            SampleSystemMetrics();
            Thread.Sleep(SampleIntervalMs);
        }
    }

    /// <summary>Refreshes CPU, memory, and available GPU measurements.</summary>
    private void SampleSystemMetrics()
    {
        try
        {
            var CpuTime = Process.TotalProcessorTime;
            long Timestamp = Stopwatch.GetTimestamp();
            double ElapsedSeconds = (Timestamp - LastSampleTimestamp) / (double)Stopwatch.Frequency;
            CpuUsage = ElapsedSeconds > 0
                ? (float)((CpuTime - LastCpuTime).TotalSeconds / ElapsedSeconds * 100 * CpuCoreCountInv)
                : 0f;
            LastCpuTime = CpuTime;
            LastSampleTimestamp = Timestamp;
        }
        catch
        {
            CpuUsage = -1f;
        }

        // GPU — individual counters can go stale as GPU processes start/stop
        if (OperatingSystem.IsWindows() && GpuCounters != null && GpuCounters.Count > 0)
        {
            float totalGpu = 0f;

            for (int i = GpuCounters.Count - 1; i >= 0; i--)
            {
                try
                {
                    totalGpu += GpuCounters[i].NextValue();
                }
                catch
                {
                    GpuCounters[i].Dispose();
                    GpuCounters.RemoveAt(i);
                }
            }

            GpuUsage = totalGpu;
        }

        // Memory - Process.Refresh() is expensive, now runs in background
        try
        {
            Process.Refresh();
            MemoryMB = Process.WorkingSet64 * BytesToMB;
            PeakMemoryMB = Process.PeakWorkingSet64 * BytesToMB;
        }
        catch
        {
            // Process may be disposed
        }
    }

    /// <summary>Creates supported platform GPU counters when available.</summary>
    private void InitializeGpuCounters()
    {
        if (!OperatingSystem.IsWindows()) return;

        GpuCounters = new List<PerformanceCounter>();

        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames();

            foreach (var instance in instanceNames)
            {
                // Filter for 3D engine instances (engtype_3D)
                if (instance.Contains("engtype_3D"))
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance);
                    GpuCounters.Add(counter);
                }
            }
        }
        catch
        {
            // GPU counters not available (older Windows or no GPU)
            GpuCounters.Clear();
        }
    }

    /// <inheritdoc/>
    public override void Update()
    {
        float Delta = Time.DeltaSeconds;
        float DeltaMs = Delta * 1000f;

        UpdateFrameHistory(DeltaMs);

        FrameCount++;
        Elapsed += Delta;

        if (Elapsed >= UpdateInterval)
        {
            Fps = FrameCount / Elapsed;
            // System metrics now sampled in background thread - no syscalls here
            FrameCount = 0;
            Elapsed = 0f;
        }

        UpdateDisplay();
    }

    /// <summary>Appends the latest frame duration to the performance history.</summary>
    private void UpdateFrameHistory(float DeltaMs)
    {
        // Subtract old value, add new value - O(1) running average
        FrameTimeSum -= FrameTimeHistory[FrameHistoryIndex];
        FrameTimeHistory[FrameHistoryIndex] = DeltaMs;
        FrameTimeSum += DeltaMs;

        FrameHistoryIndex = (FrameHistoryIndex + 1) & FrameHistoryMask;
        FrameHistoryCount = Math.Min(FrameHistorySize, FrameHistoryCount + 1);
        FrameTimeAverage = FrameTimeSum / FrameHistoryCount;
    }

    /// <summary>Refreshes the profiler's labels and graphs from cached samples.</summary>
    private void UpdateDisplay()
    {
        var gameLoop = Renderer.GameLoop;
        int TargetFps = gameLoop?.TargetFramesPerSecond ?? 144;
        float ActualFps = gameLoop?.FramesPerSecond ?? 1f;
        bool Uncapped = TargetFps <= 0;

        // Smooth FPS display - exponential moving average (less flickery)
        SmoothedDisplayFps = SmoothedDisplayFps == 0 ? ActualFps : SmoothedDisplayFps * 0.9f + ActualFps * 0.1f;

        int DisplayFps = (int)MathF.Round(SmoothedDisplayFps);

        FpsBuilder.Clear().Append("FPS: ").Append(DisplayFps);
        if (!Uncapped) FpsBuilder.Append('/').Append(TargetFps);
        Inspector.SetLabel("perf", "fps", FpsBuilder.ToString());

        FrameTimeBuilder.Clear().Append("Frame time: ").AppendFormat("{0:F2}", FrameTimeAverage).Append(" ms");
        Inspector.SetLabel("perf", "frame", FrameTimeBuilder.ToString());

        if (!Uncapped)
        {
            TargetBuilder.Clear().Append("Target: ").Append(TargetFps).Append(" fps");
            Inspector.SetLabel("perf", "target", TargetBuilder.ToString());
        }
        else
        {
            Inspector.SetLabel("perf", "target", "Target: Uncapped");
        }

        CpuBuilder.Clear().Append("CPU: ");
        if (CpuUsage < 0) CpuBuilder.Append("Unavailable");
        else CpuBuilder.AppendFormat("{0:F1}", CpuUsage).Append("%");
        Inspector.SetLabel("perf", "cpu", CpuBuilder.ToString());

        GpuBuilder.Clear().Append("GPU: ");
        if (GpuUsage < 0) GpuBuilder.Append("Unavailable");
        else GpuBuilder.AppendFormat("{0:F1}", GpuUsage).Append('%');
        Inspector.SetLabel("perf", "gpu", GpuBuilder.ToString());

        RamBuilder.Clear().Append("RAM: ").AppendFormat("{0:F1}", MemoryMB).Append(" MB");
        Inspector.SetLabel("perf", "ram", RamBuilder.ToString());

        PeakBuilder.Clear().Append("Peak: ").AppendFormat("{0:F1}", PeakMemoryMB).Append(" MB");
        Inspector.SetLabel("perf", "peak", PeakBuilder.ToString());
        Inspector.SetFrameHistory(FrameTimeHistory, FrameHistoryIndex, FrameHistoryCount, Uncapped ? 0 : 1000f / TargetFps);
    }

    #region Public API
    /// <summary>Returns the latest measured frame rate.</summary>
    public float GetFps() => Fps;

    /// <summary>Returns the average duration of the retained frame samples.</summary>
    public float GetFrameTimeAverage() => FrameTimeAverage;

    /// <summary>Returns the sampled process memory usage in megabytes.</summary>
    public float GetMemoryMB() => MemoryMB;

    /// <summary>Returns the sampled process CPU utilization.</summary>
    public float GetCpuUsage() => CpuUsage;

    /// <summary>Returns sampled GPU utilization when the platform provides it.</summary>
    public float GetGpuUsage() => GpuUsage;
    #endregion

    /// <inheritdoc/>
    public override void Render() { }

    /// <inheritdoc/>
    public override void Dispose()
    {
        // Stop sampler thread first
        SamplerRunning = false;
        SamplerThread?.Join();

        if (OperatingSystem.IsWindows() && GpuCounters != null)
        {
            foreach (var counter in GpuCounters)
                counter.Dispose();

            GpuCounters.Clear();
        }

        Process?.Dispose();
    }
}
