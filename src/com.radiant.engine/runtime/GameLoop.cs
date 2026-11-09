using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using com.radiant.engine.core;
using com.radiant.engine.input;

namespace com.radiant.engine.runtime;

/// <summary>Coordinates scene transitions, fixed updates, rendering, and frame pacing.</summary>
public partial class GameLoop : IGameObject
{
    /// <summary>Requests finer Windows timer resolution for frame pacing.</summary>
    [LibraryImport("winmm.dll")]
    private static partial uint timeBeginPeriod(uint period);

    /// <summary>Releases the Windows timer-resolution request.</summary>
    [LibraryImport("winmm.dll")]
    private static partial uint timeEndPeriod(uint period);

    private const int NoScene = -1;

    private Scene[] Scenes = [];

    private int SceneId = NoScene;

    private int NextSceneId = NoScene;

    private IRuntimeHost Host;
    private bool Initialized;
    private bool Disposed;
    private TimeSpan TotalTime;
    private TimeSpan FixedTime;

    public FrameTime Time { get; private set; }
    /// <summary>Optional caller-owned input source for replay, tests or custom devices; otherwise uses the host.</summary>
    public IInputSource InputSource { get; set; }
    public InputFrame Controls { get; private set; } = InputFrame.Empty;

    // Timing configuration
    public static readonly int[] FpsOptions = [60, 120, 144, 0];
    public static readonly string[] FpsOptionNames = ["60", "120", "144", "No Limit"];
    public int TargetFramesPerSecond { get; private set; } = 144;
    public const int UnfocusedFramesPerSecond = 30;
    public bool ThrottleUnfocused = true;

    public int TargetUpdatesPerSecond { get; private set; } = 64;

    private double UpdateInterval;

    private double FrameInterval;
    private static readonly double UnfocusedFrameInterval = 1.0 / UnfocusedFramesPerSecond;

    // Timing state
    private Stopwatch GlobalTimer = new();

    private long LastUpdateTicks;

    private long LastFrameTicks;
    private long LastFrameTimingTicks;

    private double FixedUpdateAccumulator;

    // Maximum fixed updates per frame to prevent spiral of death
    private const int MaxFixedUpdatesPerFrame = 8;

    // FPS tracking
    public float FramesPerSecond { get; private set; }
    public float FrameTimeMs { get; private set; }
    public float FrameTimeSmoothed { get; private set; }
    public float RenderTimeMs { get; private set; }
    public float RenderTimeSmoothed { get; private set; }

    private int FrameCount;

    private double LastFpsUpdate;
    private const float FrameTimeSmoothing = 0.9f; // EMA smoothing factor

    /// <summary>Starts frame timing and prepares the loop's update intervals.</summary>
    public void Initialize(IRuntimeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Initialized) throw new InvalidOperationException("The loop is already initialized.");
        Host = host;
        Initialize();
    }

    /// <summary>Starts frame timing and prepares the loop's update intervals.</summary>
    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (Initialized) throw new InvalidOperationException("The loop is already initialized.");
        Initialized = true;
        UpdateInterval = 1.0 / TargetUpdatesPerSecond;
        FrameInterval = TargetFramesPerSecond > 0 ? 1.0 / TargetFramesPerSecond : 0;

        if (OperatingSystem.IsWindows()) timeBeginPeriod(1);
        GlobalTimer.Start();
        LastUpdateTicks = GlobalTimer.ElapsedTicks;
        LastFrameTicks = GlobalTimer.ElapsedTicks;
        LastFrameTimingTicks = GlobalTimer.ElapsedTicks;
        LastFpsUpdate = GlobalTimer.Elapsed.TotalSeconds;
    }

    /// <summary>Excludes startup work from the first simulation and frame timing samples.</summary>
    internal void ResetStartupTiming()
    {
        LastUpdateTicks = GlobalTimer.ElapsedTicks;
        LastFrameTicks = LastUpdateTicks;
        LastFrameTimingTicks = LastUpdateTicks;
        LastFpsUpdate = GlobalTimer.Elapsed.TotalSeconds;
    }

    /// <summary>Changes the frame-rate cap and recalculates the render interval.</summary>
    public void SetTargetFps(int fps)
    {
        TargetFramesPerSecond = fps;
        FrameInterval = fps > 0 ? 1.0 / fps : 0;
    }

    /// <summary>Disposes active scene resources and releases platform timing resources.</summary>
    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        try { ReleaseActiveScene(); }
        finally
        {
            if (Initialized && OperatingSystem.IsWindows()) timeEndPeriod(1);
            GlobalTimer.Stop();
            GC.SuppressFinalize(this);
        }
    }

    private void ReleaseActiveScene()
    {
        if (SceneId == NoScene) return;
        var scene = Scenes[SceneId];
        SceneId = NoScene;
        try { scene.Dispose(); }
        finally { Host?.ReleaseScene(scene); }
    }

    /// <summary>Appends a scene to the game loop's registered scene list.</summary>
    public void AddScene(Scene scene)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        Scenes = [.. Scenes, scene];
    }

    /// <summary>Disposes the previous scene and initializes the requested scene and renderer.</summary>
    private void TransitionScene(int id)
    {
        foreach (var Progress in PrepareSceneSteps(id)) { }
    }

    // Desktop startup advances this iterator once per displayed frame. Other hosts
    // and subsequent transitions keep the synchronous lifecycle.
    internal IEnumerator<LoadingProgress> PrepareStartup() => PrepareSceneSteps(Scenes.Length > 0 ? 0 : NoScene).GetEnumerator();

    private IEnumerable<LoadingProgress> PrepareSceneSteps(int id)
    {
        yield return new LoadingProgress("Initializing engine...", 0);
        ReleaseActiveScene();
        SceneId = id;

        if (SceneId != NoScene)
        {
            Scenes[SceneId].Controls = Controls;
            yield return new LoadingProgress("Preparing renderer...", 0.2);
            Host?.PrepareScene(Scenes[SceneId]);
            yield return new LoadingProgress("Initializing scene...", 0.4);
            Scenes[SceneId].Initialize();
            yield return new LoadingProgress("Loading systems...", 0.6);
            Scenes[SceneId].SetupECS();
            Scenes[SceneId].ECS.Initialize();
            yield return new LoadingProgress("Loading scene...", 0.8);
            Scenes[SceneId].SetupScene();
        }

        NextSceneId = NoScene;
        yield return new LoadingProgress("Ready", 1);
    }

    /// <summary>Queues a scene change for the next update boundary.</summary>
    public void SetActiveSceneId(int id)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if ((uint)id >= (uint)Scenes.Length) throw new ArgumentOutOfRangeException(nameof(id));
        NextSceneId = id;
    }

    /// <summary>Processes scene updates and fixed ticks, then applies a pending scene transition.</summary>
    public void Update()
    {
        long currentTicks = GlobalTimer.ElapsedTicks;
        var elapsed = TimeSpan.FromSeconds((currentTicks - LastUpdateTicks) / (double)Stopwatch.Frequency);
        LastUpdateTicks = currentTicks;
        Update(elapsed);
    }

    /// <summary>Advances simulation by an explicit duration, useful for headless hosts and replay.</summary>
    public void Update(TimeSpan Elapsed)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (!Initialized) throw new InvalidOperationException("Initialize the loop before updating.");
        if (Elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(Elapsed));
        Controls = new InputFrame((InputSource ?? Host?.Input ?? NullInputSource.Instance).Capture(), Controls.Current);
        if (SceneId == NoScene && Scenes.Length > 0) TransitionScene(0);
        if (SceneId != NoScene) Scenes[SceneId].Controls = Controls;
        TotalTime += Elapsed;
        Time = new FrameTime(TotalTime, Elapsed);
        double deltaTime = Elapsed.TotalSeconds;

        // Process fixed updates with cap to prevent spiral of death
        FixedUpdateAccumulator += deltaTime;

        int iterations = 0;

        while (FixedUpdateAccumulator >= UpdateInterval && iterations++ < MaxFixedUpdatesPerFrame)
        {
            FixedTime += TimeSpan.FromSeconds(UpdateInterval);
            if (SceneId != NoScene)
            {
                Scenes[SceneId].Time = new FrameTime(FixedTime, TimeSpan.FromSeconds(UpdateInterval), true);
                Scenes[SceneId].DeltaTime = (float)(1.0 / TargetUpdatesPerSecond);
                Scenes[SceneId].InternalFixedUpdate();
            }

            FixedUpdateAccumulator -= UpdateInterval;
        }

        // Discard excess accumulated time to prevent catch-up attempts
        if (FixedUpdateAccumulator > UpdateInterval)
            FixedUpdateAccumulator = UpdateInterval;

        // Variable update with precise delta time
        if (SceneId != NoScene)
        {
            Scenes[SceneId].Time = Time;
            Scenes[SceneId].DeltaTime = (float)deltaTime;
            Scenes[SceneId].InternalUpdate();
        }

        if (NextSceneId != NoScene && NextSceneId != SceneId) TransitionScene(NextSceneId);
    }

    /// <summary>Paces the frame, draws the active scene, and updates frame-rate measurements.</summary>
    public void Render()
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (!Initialized) throw new InvalidOperationException("Initialize the loop before rendering.");
        long frameStartTicks = GlobalTimer.ElapsedTicks;

        // Frame pacing at START — waits for ideal target, accounts for all inter-frame overhead
        bool focused = Host?.IsActive ?? true;
        double activeInterval = (!focused && ThrottleUnfocused) ? UnfocusedFrameInterval : FrameInterval;

        if (activeInterval > 0)
        {
            long targetTicks = LastFrameTicks + (long)(activeInterval * Stopwatch.Frequency);

            if (frameStartTicks < targetTicks)
            {
                double remainingMs = (targetTicks - frameStartTicks) * 1000.0 / Stopwatch.Frequency;

                // Yield in small chunks instead of sleeping (avoids 15ms scheduler quantum)
                while (remainingMs > 2.0)
                {
                    Thread.Sleep(1);
                    remainingMs = (targetTicks - GlobalTimer.ElapsedTicks) * 1000.0 / Stopwatch.Frequency;
                }

                // Short spin-wait for precision (< 2ms)
                while (GlobalTimer.ElapsedTicks < targetTicks)
                    Thread.SpinWait(10);

                // Anchor to ideal target — prevents drift from overshoot accumulation
                LastFrameTicks = targetTicks;
            }
            else
            {
                // Behind schedule — reset to NOW, don't try to catch up
                LastFrameTicks = frameStartTicks;
            }
        }
        else
        {
            LastFrameTicks = frameStartTicks;
        }

        // Frame-to-frame timing (measured before pacing wait, includes all overhead)
        FrameTimeMs = (float)((frameStartTicks - LastFrameTimingTicks) * 1000.0 / Stopwatch.Frequency);
        FrameTimeSmoothed = FrameTimeSmoothed * FrameTimeSmoothing + FrameTimeMs * (1f - FrameTimeSmoothing);
        LastFrameTimingTicks = frameStartTicks;

        // FPS calculation (0.5s window)
        FrameCount++;

        double now = GlobalTimer.Elapsed.TotalSeconds;

        if (now - LastFpsUpdate >= 0.5)
        {
            FramesPerSecond = (float)(FrameCount / (now - LastFpsUpdate));
            FrameCount = 0;
            LastFpsUpdate = now;
        }

        // Actual rendering
        long renderStartTicks = GlobalTimer.ElapsedTicks;

        if (SceneId != NoScene)
        {
            Host?.BeginFrame(Scenes[SceneId]);
            Scenes[SceneId].Time = Time;
            Scenes[SceneId].InternalRender();
            Scenes[SceneId].InternalLateRender();
            Host?.EndFrame(Scenes[SceneId]);
        }

        long renderEndTicks = GlobalTimer.ElapsedTicks;
        RenderTimeMs = (float)((renderEndTicks - renderStartTicks) * 1000.0 / Stopwatch.Frequency);
        RenderTimeSmoothed = RenderTimeSmoothed * FrameTimeSmoothing + RenderTimeMs * (1f - FrameTimeSmoothing);
    }

    /// <summary>Reserved loop-level fixed-update hook.</summary>
    public void FixedUpdate() { }

    /// <summary>Reserved loop-level overlay hook.</summary>
    public void LateRender() { }
}
