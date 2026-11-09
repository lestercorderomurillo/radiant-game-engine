using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Defines a triangle shape with optional border.</summary>
[ComponentDescription("Defines a triangle shape with optional border.")]
public struct Triangle2D : Component
{
    public Vector2 Size;
    public bool Bordered;

    /// <summary>Creates a triangle component with its default dimensions and appearance.</summary>
    public Triangle2D(Vector2? size = null, bool bordered = false)
    {
        Size = size ?? new Vector2(1.0f);
        Bordered = bordered;
    }
}
