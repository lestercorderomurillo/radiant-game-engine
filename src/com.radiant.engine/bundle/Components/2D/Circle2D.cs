using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Draws the entity as a circle centered on its <c>Transform2D</c> position. Pair it with a <c>Material</c> for color and light.</summary>
[ComponentDescription("Defines a circle shape with a radius.")]
public struct Circle2D : Component
{
    /// <summary>Radius in world units.</summary>
    public float Radius;

    /// <summary>Creates a circle with the given radius.</summary>
    public Circle2D(float radius = 1.0f)
    {
        Radius = radius;
    }
}
