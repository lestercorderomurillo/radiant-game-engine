using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using com.radiant.engine.runtime;

// This consumer uses only Radiant and BCL APIs. It never initializes a graphics device.
internal static class RuntimeBackendRegression
{
    public static void Run()
    {
        var scene = new SimulationScene();
        new GameClient(new HeadlessBackend(64, TimeSpan.FromSeconds(1.0 / 64))).Run(scene);
        Require(scene.Probe.Updates == 64 && scene.Probe.FixedUpdates == 64, "Headless ECS tick counts");
        Require(scene.Probe.Disposals == 1 && scene.Probe.Renders == 0, "Headless lifecycle");
        Require(scene.Time.Total == TimeSpan.FromSeconds(1), "Backend-independent time");
        Require(scene.Probe.LastFixed.Total == TimeSpan.FromSeconds(1) && scene.Probe.LastFixed.IsFixedStep, "Fixed clock");

        var events = new List<string>();
        var first = new SimulationScene(events);
        var second = new SimulationScene(events);
        var backend = new RecordingBackend(events, second, false);
        new GameClient(backend).Run(first);
        Require(first.Probe.Disposals == 1 && second.Probe.Disposals == 1, "Scene transition disposal");
        Require(string.Join(",", events) == "prepare,system-dispose,release,prepare,begin,render,end,system-dispose,release,host-dispose", "Host ordering: " + string.Join(",", events));

        events.Clear();
        var failing = new SimulationScene(events);
        try { new GameClient(new RecordingBackend(events, null, true)).Run(failing); throw new Exception("Expected backend error"); }
        catch (ApplicationException) { }
        Require(failing.Probe.Disposals == 1, "Failed run releases systems");
        Require(string.Join(",", events) == "prepare,system-dispose,release,host-dispose", "Failure cleanup ordering");

        using var loop = new GameLoop();
        loop.Initialize();
        var capped = new SimulationScene();
        loop.AddScene(capped);
        loop.Update(TimeSpan.FromSeconds(1));
        Require(capped.Probe.FixedUpdates == 8, "Fixed-update catch-up cap");
        try { loop.Update(TimeSpan.FromTicks(-1)); throw new Exception("Negative time accepted"); } catch (ArgumentOutOfRangeException) { }
        loop.Dispose();
        loop.Dispose();
        Require(capped.Probe.Disposals == 1, "Idempotent disposal");
        try { loop.Update(TimeSpan.Zero); throw new Exception("Disposed loop accepted"); } catch (ObjectDisposedException) { }
        Console.WriteLine("PASS: Radiant-only runtime API, headless ECS, fixed timing, interchangeable host, scene transitions and failure cleanup.");
    }

    private static void Require(bool Condition, string Message)
    {
        if (!Condition) throw new Exception(Message);
    }

    private sealed class SimulationScene(List<string> Events = null) : Scene
    {
        public Probe Probe;
        public override void SetupECS()
        {
            Probe = ECS.AddSystem(new Probe(Events));
        }
    }

    private sealed class Probe(List<string> Events) : com.radiant.engine.core.System
    {
        public int Updates, FixedUpdates, Disposals, Renders;
        public FrameTime LastFixed;
        public override void Update()
        {
            Require(!Time.IsFixedStep && Time == Scene.Time && Time.DeltaSeconds == Scene.DeltaTime, "Update timing propagation");
            Updates++;
        }
        public override void FixedUpdate()
        {
            Require(Time.IsFixedStep && Time.DeltaSeconds == 1f / 64, "Fixed timing propagation");
            LastFixed = Time;
            FixedUpdates++;
        }
        public override void Render() { Events?.Add("render"); Renders++; }
        public override void Dispose() { Events?.Add("system-dispose"); Disposals++; }
    }

    private sealed class RecordingBackend(List<string> Events, Scene Next, bool Fail) : IRuntimeBackend
    {
        public IRuntimeHost CreateHost(GameLoop Loop, WindowOptions Options) => new Host(Loop, Events, Next, Fail);
        private sealed class Host(GameLoop Loop, List<string> Events, Scene Next, bool Fail) : IRuntimeHost
        {
            public bool IsActive => true;
            public void Run()
            {
                Loop.Initialize(this);
                Loop.SetTargetFps(0);
                Loop.Update(TimeSpan.FromSeconds(1.0 / 64));
                if (Fail) throw new ApplicationException("Backend failure");
                Loop.AddScene(Next);
                Loop.SetActiveSceneId(1);
                Loop.Update(TimeSpan.Zero);
                Loop.Render();
            }
            public void PrepareScene(Scene Scene) => Events.Add("prepare");
            public void ReleaseScene(Scene Scene) => Events.Add("release");
            public void BeginFrame(Scene Scene) => Events.Add("begin");
            public void EndFrame(Scene Scene) => Events.Add("end");
            public void Dispose() => Events.Add("host-dispose");
        }
    }
}
