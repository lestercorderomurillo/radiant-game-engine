using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Defines a circle shape with a radius.</summary>
[ComponentDescription("Defines a circle shape with a radius.")]
public struct Circle2D : Component
{
    public float Radius;

    /// <summary>Creates a circle component with its default dimensions and appearance.</summary>
    public Circle2D(float radius = 1.0f)
    {
        Radius = radius;
    }
}
