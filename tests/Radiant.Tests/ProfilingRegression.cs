using System;
using System.Collections.Generic;
using System.Reflection;
using com.radiant.engine.bundle;
using com.radiant.engine.core;

internal static class ProfilingRegression
{
    public static void Run(IRenderer renderer)
    {
        // Startup averages must not include the unfilled portion of the ring.
        var monitor = new PerformanceMonitor();
        var sample = typeof(PerformanceMonitor).GetMethod("UpdateFrameHistory", BindingFlags.NonPublic | BindingFlags.Instance);
        sample.Invoke(monitor, [20f]);
        if (monitor.GetFrameTimeAverage() != 20f) throw new Exception("Profiling averaged uninitialized samples.");
        for (int i = 0; i < 64; i++) sample.Invoke(monitor, [10f]);
        if (monitor.GetFrameTimeAverage() != 10f) throw new Exception("Profiling retained a sample after history rollover.");

        using var scene = new Scene { Renderer = renderer };
        scene.Initialize();
        var inspector = scene.ECS.AddSystem(new Inspector { StartupWorkspaceMode = WorkspaceMode.Windows, ShowAllOnStartup = false, Visible = false });
        inspector.Initialize();
        Inspector.CreateWindow("perf", "Profiling");
        Inspector.AddSectionLabel("perf", "perfHeader", "Performance");
        Inspector.AddMetric("perf", "fps", "FPS: 60");
        Inspector.AddSectionLabel("perf", "geometryHeader", "Geometry");
        Inspector.AddMetric("perf", "emissive", "Emissive: 12");
        Inspector.SetFrameHistory([10f, 20f, 30f], 0, 3, 16.67f);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var windows = (Dictionary<string, WindowData>)typeof(Inspector).GetField("Windows", flags).GetValue(inspector);
        var layout = typeof(Inspector).GetMethod("ComputeLayout", flags);
        var window = windows["perf"];
        Widget Get(string id) => window.Widgets[window.WidgetIndex[id]];
        window.Size = new Vector2(1100, 0);
        layout.Invoke(inspector, [window]);
        if (Get("fps").Bounds.Y != Get("emissive").Bounds.Y || Get("fps").Bounds.Right >= Get("emissive").Bounds.Left)
            throw new Exception("Wide profiling does not place metrics in aligned columns.");
        if (Get("history").Bounds.Y <= Get("emissive").Bounds.Bottom)
            throw new Exception("Profiling history overlaps its metrics.");
        window.Size = new Vector2(500, 0);
        layout.Invoke(inspector, [window]);
        if (Get("geometryHeader").Bounds.Y <= Get("fps").Bounds.Bottom || Get("fps").Bounds.X != Get("emissive").Bounds.X)
            throw new Exception("Narrow profiling does not stack its columns.");
        Console.WriteLine("PASS: profiling startup average, history rollover, wide columns and narrow stacked metrics.");
    }
}
