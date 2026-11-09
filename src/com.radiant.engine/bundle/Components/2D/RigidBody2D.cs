using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Mass for your own physics code; the engine stores it but does not simulate it.</summary>
[ComponentDescription("Physical weight for 2D rigid body simulation.")]
public struct RigidBody2D : Component
{
    /// <summary>Mass of the body.</summary>
    public float Weight;

    /// <summary>Creates a body with the default weight.</summary>
    public RigidBody2D()
    {
        Weight = 1.0f;
    }
}
