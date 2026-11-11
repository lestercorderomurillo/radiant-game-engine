namespace com.radiant.engine.core;

/// <summary>A renderer-owned 2D viewport that editor tools can pan without an ECS camera entity.</summary>
/// <remarks>Use ICameraView for entity cameras with zoom and rotation. This adapter exposes translation only.</remarks>
public interface IViewportCamera2D
{
    string CameraName { get; }
    Vector2 CameraPosition { get; set; }
}
