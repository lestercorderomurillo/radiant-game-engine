using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>A 2D camera. Pan with <c>Position</c>, zoom in with <c>Zoom</c> above 1, and tilt with <c>Rotation</c>.</summary>
[ComponentDescription("2D camera with position, zoom and rotation.")]
public struct Camera2D : Component
{
    /// <summary>World point at the center of the view.</summary>
    public Vector2 Position;
    /// <summary>Magnification: 1 shows the world at its natural size, 2 is twice as close.</summary>
    public float Zoom;
    /// <summary>View rotation in radians.</summary>
    public float Rotation;

    /// <summary>Creates a camera at the origin with no zoom or rotation.</summary>
    public Camera2D()
    {
        Position = Vector2.Zero;
        Zoom = 1f;
        Rotation = 0f;
    }

    /// <summary>Returns this camera moved and rotated to match an entity, so the view can follow it.</summary>
    public readonly Camera2D WithTransform(in Transform2D transform) => new() { Position = transform.Position, Rotation = transform.Rotation, Zoom = Zoom };
}
