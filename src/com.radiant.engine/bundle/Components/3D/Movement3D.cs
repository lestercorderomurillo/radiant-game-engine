using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

[ComponentDescription("Velocity and acceleration for 3D movement.")]
public struct Movement3D : Component
{
    public Vector3 Speed;
    public Vector3 Acceleration;
}
