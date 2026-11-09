using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Velocity and acceleration in 3D. Add <c>Movement3DSystem</c> to the scene and entities with this component move on their own.</summary>
[ComponentDescription("Velocity and acceleration for 3D movement.")]
public struct Movement3D : Component
{
    /// <summary>Velocity in world units per second.</summary>
    public Vector3 Speed;
    /// <summary>Change in velocity per second, such as gravity.</summary>
    public Vector3 Acceleration;
}
