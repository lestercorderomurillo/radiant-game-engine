using System;
using System.Threading;
using System.Threading.Tasks;

namespace com.radiant.engine.runtime;

/// <summary>How the game window opens. The world keeps the same coordinates whatever size the window has.</summary>
public sealed class WindowOptions
{
    /// <summary>Text in the title bar.</summary>
    public string Title { get; init; } = "Radiant";
    /// <summary>Starting width in pixels.</summary>
    public int Width { get; init; } = 1280;
    /// <summary>Starting height in pixels.</summary>
    public int Height { get; init; } = 720;
    /// <summary>Opens the window maximized.</summary>
    public bool Maximized { get; init; }
    /// <summary>Shows Radiant's branded progress screen during startup. Does not disable startup work.</summary>
    public bool ShowLoader { get; init; } = true;
    /// <summary>Optional asynchronous preparation (for example update checking). Runs before scene creation;
    /// report progress through the supplied sink, honor cancellation, and never access GPU resources here.</summary>
    public Func<IProgress<LoadingProgress>, CancellationToken, Task> PrepareApplication { get; init; }
}
