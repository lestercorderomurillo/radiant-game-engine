namespace com.radiant.engine.runtime;

/// <summary>Initial desktop window settings. World coordinates remain independent of window size.</summary>
public sealed class WindowOptions
{
    public string Title { get; init; } = "Radiant";
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
}
