using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Where a 2D entity is, how it is rotated and scaled, and which layer it draws on.</summary>
[ComponentDescription("2D position, rotation in radians, scale and independent draw order.")]
public struct Transform2D : Component
{
    /// <summary>World position. Move entities with <c>ECS.SetPosition</c> to keep spatial queries accurate.</summary>
    public Vector2 Position;
    /// <summary>Rotation in radians.</summary>
    public float Rotation;
    /// <summary>Size multiplier per axis.</summary>
    public Vector2 Scale;
    /// <summary>Draw order: higher layers draw on top.</summary>
    public float Layer;

    /// <summary>Creates a transform at the origin with no rotation and normal scale.</summary>
    public Transform2D() { Position = Vector2.Zero; Rotation = 0; Scale = Vector2.One; Layer = 0; }
}
