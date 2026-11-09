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
}
