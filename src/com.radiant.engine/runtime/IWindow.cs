namespace com.radiant.engine.runtime;

/// <summary>Application-facing window operations, independent of the desktop toolkit.</summary>
public interface IWindow
{
    bool IsActive { get; }
    string Title { get; set; }
    /// <summary>Client-area position and size in screen pixels.</summary>
    Rectangle ClientBounds { get; }
    /// <summary>Requests normal loop shutdown; resources are released by the host.</summary>
    void RequestClose();
}
