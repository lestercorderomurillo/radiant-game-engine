using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Defines a rectangle shape with a size.</summary>
[ComponentDescription("Defines a rectangle shape with a size.")]
public struct Rectangle2D : Component
{
    public Vector2 Size;

    /// <summary>Creates a rectangle component with its default dimensions and appearance.</summary>
    public Rectangle2D(Vector2? size)
    {
        Size = size ?? new Vector2(1.0f);
    }
}
