using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Velocity and acceleration for 2D movement.</summary>
[ComponentDescription("Velocity and acceleration for 2D movement.")]
public struct Movement2D : Component
{
    public Vector2 Speed;
    public Vector2 Acceleration;

    /// <summary>Starts movement with zero velocity and acceleration.</summary>
    public Movement2D()
    {
        Speed = new Vector2();
        Acceleration = new Vector2();
    }
}
