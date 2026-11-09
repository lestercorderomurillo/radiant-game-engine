using System;
using com.radiant.engine.runtime;

namespace com.radiant.engine.core;

/// <summary>
/// A level, menu or demo: one set of systems and entities with its own lifecycle. Subclass it, register
/// systems in <c>SetupECS</c>, create entities in <c>SetupScene</c>, and hand it to <c>GameClient.Run</c>.
/// </summary>
/// <example>
/// The following example shows how to build a lit scene with one warm light:
/// <code>
/// public class Campfire : Scene
/// {
///     public override void SetupECS()
///     {
///         ECS.AddSystem&lt;Geometry&gt;();
///         ECS.AddSystem&lt;HRCGI&gt;();
///     }
///
///     public override void SetupScene()
///     {
///         var warm = new Color(255, 170, 90);
///         LightFactory.CreateLight(ECS, Renderer.VirtualSize / 2, 80f, warm, warm);
///     }
/// }
///
/// new GameClient().Run(new Campfire());
/// </code>
/// </example>
public class Scene : IGameObject
{
    /// <summary>Index of this scene in the game loop.</summary>
    public int Id { get; private set; }

    /// <summary>The scene's entities and systems. Available from <c>SetupECS</c> onward.</summary>
    public ECS ECS { get; private set; }

    /// <summary>Timing for the current callback: <c>Time.DeltaSeconds</c> is the step to advance by.</summary>
    public FrameTime Time { get; set; }

    /// <summary>Keyboard, mouse and text input for this frame.</summary>
    public com.radiant.engine.input.InputFrame Controls { get; internal set; } = com.radiant.engine.input.InputFrame.Empty;

    /// <summary>Seconds since the previous callback; the same value as <c>Time.DeltaSeconds</c>.</summary>
    public float DeltaTime { get; set; }

    /// <summary>Draws shapes, sprites, text and shader passes. Null when running headless.</summary>
    public IRenderer Renderer { get; set; }

    /// <summary>Creates the scene's ECS. The game loop calls this before <c>SetupECS</c>.</summary>
    public void Initialize()
    {
        ECS = new ECS(this, Renderer);
    }

    /// <summary>Releases every system and entity. <c>GameClient.Run</c> does this when the window closes.</summary>
    public void Dispose()
    {
        ECS?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Register your systems here. The engine sorts and initializes them once this returns; no base call is needed.</summary>
    public virtual void SetupECS() { }

    /// <summary>Create your entities, lights and windows here. Systems are already initialized; no base call is needed.</summary>
    public virtual void SetupScene() { }

    /// <summary>Runs every frame before the systems' <c>Update</c>. A good place for scene-wide input such as pausing.</summary>
    public virtual void Update() { }

    /// <summary>Runs 64 times per second before the systems' <c>FixedUpdate</c>. Put deterministic simulation here.</summary>
    public virtual void FixedUpdate() { }

    /// <summary>Runs every frame before the systems' <c>Render</c>.</summary>
    public virtual void Render() { }

    /// <summary>Runs every frame before the systems' <c>LateRender</c>, after all main rendering.</summary>
    public virtual void LateRender() { }

    /// <summary>Runs the scene update followed by the ECS update.</summary>
    internal void InternalUpdate()
    {
        Update();
        ECS.Update();
    }

    /// <summary>Runs the scene fixed update followed by the ECS fixed update.</summary>
    internal void InternalFixedUpdate()
    {
        FixedUpdate();
        ECS.FixedUpdate();
    }

    /// <summary>Runs scene drawing followed by the ECS render pass.</summary>
    internal void InternalRender()
    {
        Render();
        ECS.Render();
    }

    /// <summary>Runs scene overlays followed by the ECS late-render pass.</summary>
    internal void InternalLateRender()
    {
        LateRender();
        ECS.LateRender();
    }
}
