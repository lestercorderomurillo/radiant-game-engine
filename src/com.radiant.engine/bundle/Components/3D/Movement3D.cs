using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Velocity and acceleration for 3D movement.</summary>
[ComponentDescription("Velocity and acceleration for 3D movement.")]
public struct Movement3D : Component
{
    public Vector3 Speed;
    public Vector3 Acceleration;
}
