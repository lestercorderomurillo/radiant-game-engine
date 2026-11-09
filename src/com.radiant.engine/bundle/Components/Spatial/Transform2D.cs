using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>2D position, rotation in radians, scale and independent draw order.</summary>
[ComponentDescription("2D position, rotation in radians, scale and independent draw order.")]
public struct Transform2D : Component
{
    public Vector2 Position;
    public float Rotation;
    public Vector2 Scale;
    public float Layer;

    /// <summary>Creates a 2D transform with default position, rotation, and scale.</summary>
    public Transform2D() { Position = Vector2.Zero; Rotation = 0; Scale = Vector2.One; Layer = 0; }
}
