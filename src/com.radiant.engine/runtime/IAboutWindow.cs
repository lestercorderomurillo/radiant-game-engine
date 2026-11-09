namespace com.radiant.engine.runtime;

/// <summary>Optional host capability for showing the engine's modal About panel.</summary>
public interface IAboutWindow
{
    /// <summary>Displays the panel using the inspector's portable theme colors.</summary>
    void ShowAbout(Color closeColor, Color closeHover, Color closeText);
}
