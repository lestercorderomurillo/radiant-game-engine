using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Velocity and acceleration. Add <c>Movement2DSystem</c> to the scene and entities with this component move on their own.</summary>
/// <example>
/// The following example shows how to launch a ball that falls under gravity:
/// <code>
/// ECS.AddSystem&lt;Movement2DSystem&gt;();
///
/// int ball = ECS.CreateEntity(new Vector2(100, 100));
/// ECS.AddComponent&lt;Movement2D&gt;(ball) = new Movement2D { Speed = new Vector2(300, -200), Acceleration = new Vector2(0, 600) };
/// </code>
/// </example>
[ComponentDescription("Velocity and acceleration for 2D movement.")]
public struct Movement2D : Component
{
    /// <summary>Velocity in world units per second.</summary>
    public Vector2 Speed;
    /// <summary>Change in velocity per second, such as gravity.</summary>
    public Vector2 Acceleration;

    /// <summary>Starts at rest.</summary>
    public Movement2D()
    {
        Speed = new Vector2();
        Acceleration = new Vector2();
    }
}
