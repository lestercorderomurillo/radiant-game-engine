using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Axis-aligned collision bounds for 2D entities.</summary>
[ComponentDescription("Axis-aligned collision bounds for 2D entities.")]
public struct Collision2D : Component
{
    public Vector2 Bounds;

    /// <summary>Creates collision data with the component's default shape and response settings.</summary>
    public Collision2D()
    {
        Bounds = new Vector2(1.0f);
    }
}
