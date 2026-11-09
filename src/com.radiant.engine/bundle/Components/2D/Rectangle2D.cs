using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Draws the entity as a rectangle whose top-left corner sits at its <c>Transform2D</c> position. Pair it with a <c>Material</c>.</summary>
[ComponentDescription("Defines a rectangle shape with a size.")]
public struct Rectangle2D : Component
{
    /// <summary>Width and height in world units.</summary>
    public Vector2 Size;

    /// <summary>Creates a rectangle of the given size.</summary>
    public Rectangle2D(Vector2? size)
    {
        Size = size ?? new Vector2(1.0f);
    }
}
