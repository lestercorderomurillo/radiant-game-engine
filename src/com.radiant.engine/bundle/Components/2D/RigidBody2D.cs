using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Physical weight for 2D rigid body simulation.</summary>
[ComponentDescription("Physical weight for 2D rigid body simulation.")]
public struct RigidBody2D : Component
{
    public float Weight;

    /// <summary>Creates rigid-body data with the component's default physical settings.</summary>
    public RigidBody2D()
    {
        Weight = 1.0f;
    }
}
