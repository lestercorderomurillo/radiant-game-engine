using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>A collision box for your own collision logic; the engine stores it but does not resolve collisions.</summary>
[ComponentDescription("Axis-aligned collision bounds for 2D entities.")]
public struct Collision2D : Component
{
    /// <summary>Box width and height in world units.</summary>
    public Vector2 Bounds;

    /// <summary>Creates an empty collision box.</summary>
    public Collision2D()
    {
        Bounds = new Vector2(1.0f);
    }
}
