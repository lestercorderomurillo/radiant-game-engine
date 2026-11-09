using System;
using System.Threading;
using System.Threading.Tasks;

namespace com.radiant.engine.runtime;

/// <summary>Initial desktop window settings. World coordinates remain independent of window size.</summary>
public sealed class WindowOptions
{
    public string Title { get; init; } = "Radiant";
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool Maximized { get; init; }
    /// <summary>Shows Radiant's branded progress screen during startup. Does not disable startup work.</summary>
    public bool ShowLoader { get; init; } = true;
    /// <summary>Optional asynchronous preparation (for example update checking). Runs before scene creation;
    /// report progress through the supplied sink, honor cancellation, and never access GPU resources here.</summary>
    public Func<IProgress<LoadingProgress>, CancellationToken, Task> PrepareApplication { get; init; }
}
