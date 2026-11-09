using System;
using com.radiant.engine.core;

namespace com.radiant.engine.runtime;

/// <summary>Runs a finite, deterministic simulation without a window, graphics device or render callbacks.</summary>
public sealed class HeadlessBackend : IRuntimeBackend
{
    public int Updates { get; }
    public TimeSpan Step { get; }

    public HeadlessBackend(int Updates, TimeSpan Step)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Updates);
        if (Step <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(Step));
        this.Updates = Updates;
        this.Step = Step;
    }

    public IRuntimeHost CreateHost(GameLoop Loop, WindowOptions Options) => new Host(Loop, Updates, Step);

    private sealed class Host(GameLoop Loop, int Updates, TimeSpan Step) : IRuntimeHost
    {
        public bool IsActive => true;
        public void Run()
        {
            Loop.Initialize(this);
            for (int index = 0; index < Updates; index++) Loop.Update(Step);
        }
        public void PrepareScene(Scene Scene) { }
        public void ReleaseScene(Scene Scene) { }
        public void BeginFrame(Scene Scene) { }
        public void EndFrame(Scene Scene) { }
        public void Dispose() { }
    }
}
