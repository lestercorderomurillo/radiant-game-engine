using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Draws the entity as a triangle at its <c>Transform2D</c> position, filled or as an outline.</summary>
[ComponentDescription("Defines a triangle shape with optional border.")]
public struct Triangle2D : Component
{
    /// <summary>Width and height in world units.</summary>
    public Vector2 Size;
    /// <summary>Draws only the outline when true.</summary>
    public bool Bordered;

    /// <summary>Creates a filled or outlined triangle of the given size.</summary>
    public Triangle2D(Vector2? size = null, bool bordered = false)
    {
        Size = size ?? new Vector2(1.0f);
        Bordered = bordered;
    }
}
