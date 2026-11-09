using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using com.radiant.engine.core;
using com.radiant.engine.runtime;

internal static class StartupRegression
{
    public static void Run()
    {
        foreach (double Invalid in new[] { -0.1, 1.1, double.NaN, double.PositiveInfinity })
        {
            try { _ = new LoadingProgress("Invalid", Invalid); throw new Exception("Invalid progress accepted"); }
            catch (ArgumentOutOfRangeException) { }
        }
        using var Loop = new GameLoop();
        var Scene = new ProbeScene();
        Loop.AddScene(Scene);
        Loop.Initialize();
        using var Gate = new ManualResetEventSlim();
        using var Reported = new ManualResetEventSlim();
        using var Startup = new StartupSession(Loop, new WindowOptions
        {
            PrepareApplication = (Progress, Token) =>
            {
                Progress.Report(new LoadingProgress("Checking for updates..."));
                Reported.Set();
                Gate.Wait(Token);
                return Task.CompletedTask;
            }
        });
        Startup.Advance();
        Require(Reported.Wait(TimeSpan.FromSeconds(5)), "Preparation did not start");
        Startup.Advance();
        Require(Scene.ECS == null && !Startup.Finished, "Scene started before preparation finished");
        Require(Startup.Progress.Message == "Checking for updates..." && Startup.Progress.Completion == null, "Unknown progress lost");
        Gate.Set();
        WaitUntil(() => Startup.CanAdvanceSynchronously);
        var Stages = new List<double>();
        while (!Startup.Finished)
        {
            Startup.Advance();
            if (!Startup.Finished) Stages.Add(Startup.Progress.Completion.Value);
            Require(Scene.Updates == 0, "Simulation advanced during startup");
        }
        Require(Stages.SequenceEqual(new double[] { 0, 0.2, 0.4, 0.6, 0.8, 1 }), "Startup milestone order");
        Require(Scene.Setups == 1, "Scene setup did not run once");
        Startup.Report(new LoadingProgress("Late background report", 0));
        Require(Startup.Progress.Message == "Ready", "Late update replaced scene progress");
        Loop.Update(TimeSpan.Zero);
        Require(Scene.Updates == 1 && Scene.Setups == 1, "Scene handoff repeated setup");

        using var FailedLoop = new GameLoop();
        FailedLoop.Initialize();
        using var Failed = new StartupSession(FailedLoop, new WindowOptions
        {
            PrepareApplication = (_, _) => Task.FromException(new InvalidOperationException("Expected update failure"))
        });
        Failed.Advance();
        WaitUntil(() => Failed.CanAdvanceSynchronously);
        Failed.Advance();
        Require(Failed.Error is InvalidOperationException && !Failed.Finished, "Failure incorrectly continued startup");

        using var CancelLoop = new GameLoop();
        CancelLoop.Initialize();
        using var Started = new ManualResetEventSlim();
        using var Cancelled = new ManualResetEventSlim();
        var Cancel = new StartupSession(CancelLoop, new WindowOptions
        {
            PrepareApplication = async (_, Token) =>
            {
                Started.Set();
                try { await Task.Delay(Timeout.Infinite, Token); }
                finally { Cancelled.Set(); }
            }
        });
        Cancel.Advance();
        Require(Started.Wait(TimeSpan.FromSeconds(5)), "Cancel callback did not start");
        Cancel.Dispose();
        Cancel.Dispose();
        Require(Cancelled.Wait(TimeSpan.FromSeconds(5)), "Window disposal did not cancel preparation");
        Console.WriteLine("PASS: startup milestones, update gating, unknown progress, failure, cancellation, late reports and scene handoff.");
    }

    private static void WaitUntil(Func<bool> Condition)
    {
        if (!SpinWait.SpinUntil(Condition, TimeSpan.FromSeconds(5))) throw new Exception("Startup timed out");
    }

    private static void Require(bool Condition, string Message)
    {
        if (!Condition) throw new Exception(Message);
    }

    private sealed class ProbeScene : Scene
    {
        public int Setups, Updates;
        public override void SetupScene() => Setups++;
        public override void Update() => Updates++;
    }
}
