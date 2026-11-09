using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>2D camera with position, zoom and rotation.</summary>
[ComponentDescription("2D camera with position, zoom and rotation.")]
public struct Camera2D : Component
{
    public Vector2 Position;
    public float Zoom;
    public float Rotation;

    /// <summary>Creates a 2D camera with the component's default viewing settings.</summary>
    public Camera2D()
    {
        Position = Vector2.Zero;
        Zoom = 1f;
        Rotation = 0f;
    }

    /// <summary>Returns camera data positioned and oriented by the supplied entity transform.</summary>
    public readonly Camera2D WithTransform(in Transform2D transform) => new() { Position = transform.Position, Rotation = transform.Rotation, Zoom = Zoom };
}
