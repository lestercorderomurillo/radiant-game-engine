using com.radiant.engine.core;
using com.radiant.engine.runtime;
using com.radiant.engine.input;

namespace com.radiant.engine.bundle;

/// <summary>Moves an emissive entity with the mouse in world coordinates.</summary>
public class MouseLight : core.System
{
    public float Radius { get; set; } = 100f;
    public Color Albedo { get; set; } = new Color(0, 0, 0, 128);
    public Color Emissive { get; set; } = new Color(0, 0, 0, 255);
    public float Z { get; set; } = 65535f;

    public int EntityId { get; private set; }

    /// <inheritdoc/>
    public override void Initialize()
    {
        var mouse = Controls.Mouse;
        var worldPos = Renderer.ScreenToWorld(new Vector2(mouse.X, mouse.Y));

        EntityId = LightFactory.CreateLight(Scene.ECS, worldPos, Radius, Albedo, Emissive, Z);
        Scene.ECS.AddComponent<MotionTrackable>(EntityId);
    }

    /// <inheritdoc/>
    public override void Update()
    {
        var mouse = Controls.Mouse;
        var worldPos = Renderer.ScreenToWorld(new Vector2(mouse.X, mouse.Y));

        ref var transform = ref Scene.ECS.GetComponent<Transform2D>(EntityId);
        transform.Position = worldPos;
        transform.Layer = Z;
    }
}
