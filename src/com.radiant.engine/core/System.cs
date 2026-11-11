using System;
using com.radiant.engine.runtime;

namespace com.radiant.engine.core;

/// <summary>Runs this system after the listed systems, so it sees their results in the same frame.</summary>
/// <example>
/// The following example shows how to light the scene after its geometry is built:
/// <code>
/// [RunAfter(typeof(Geometry))]
/// public class CustomRenderer : System { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RunAfterAttribute : Attribute
{
    /// <summary>The system types named in the attribute.</summary>
    public Type[] SystemTypes { get; }

    /// <summary>Runs the annotated system after each of these system types.</summary>
    public RunAfterAttribute(params Type[] systemTypes) => SystemTypes = systemTypes;
}

/// <summary>Runs this system before the listed systems, so they see its results in the same frame.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RunBeforeAttribute : Attribute
{
    /// <summary>The system types named in the attribute.</summary>
    public Type[] SystemTypes { get; }

    /// <summary>Runs the annotated system before each of these system types.</summary>
    public RunBeforeAttribute(params Type[] systemTypes) => SystemTypes = systemTypes;
}

/// <summary>Which pause switch affects a <c>[Pausable]</c> system. Combine values to respond to both.</summary>
[Flags]
public enum PauseGroup : byte
{
    /// <summary>Never paused.</summary>
    None = 0,
    /// <summary>Paused by <c>ECS.GameplayPaused</c>.</summary>
    Gameplay = 1,
    /// <summary>Paused by <c>ECS.AnimationPaused</c>.</summary>
    Animation = 2
}

/// <summary>
/// Lets the scene pause this system. Plain <c>[Pausable]</c> follows <c>ECS.GameplayPaused</c>;
/// <c>[Pausable(PauseGroup.Animation)]</c> follows <c>ECS.AnimationPaused</c>.
/// </summary>
/// <example>
/// The following example shows a movement system that stops while the game is paused:
/// <code>
/// [Pausable]
/// public class EnemyMovement : System { }
///
/// ECS.GameplayPaused = true; // EnemyMovement stops; rendering and UI keep running
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public class PausableAttribute : Attribute
{
    /// <summary>The pause switches this system follows.</summary>
    public PauseGroup Groups { get; }

    /// <summary>Pauses the system with gameplay.</summary>
    public PausableAttribute() => Groups = PauseGroup.Gameplay;

    /// <summary>Pauses the system with the given switches.</summary>
    public PausableAttribute(PauseGroup groups) => Groups = groups;
}

/// <summary>Marks engine infrastructure: hidden from the System Inspector so it can't be switched off by accident.</summary>
[AttributeUsage(AttributeTargets.Class)]
public class CoreSystemAttribute : Attribute { }

/// <summary>Adds categories, such as <c>"2D"</c> or <c>"Rendering"</c>, that the System Inspector shows and filters by.</summary>
[AttributeUsage(AttributeTargets.Class)]
public class SystemTagAttribute : Attribute
{
    /// <summary>The categories shown in the System Inspector.</summary>
    public string[] Tags { get; }

    /// <summary>Tags the system with one or more categories.</summary>
    public SystemTagAttribute(params string[] tags) => Tags = tags;
}

/// <summary>
/// Where a system's <c>LateRender</c> draws: <c>World</c> for scene content, <c>Gameplay</c> (the default) for
/// game visuals, <c>Overlay</c> for debug drawing and <c>UI</c> for interface on top of everything.
/// </summary>
public enum RenderLayer : byte { World = 0, Gameplay = 1, Overlay = 2, UI = 3 }

/// <summary>
/// Game behavior that runs every frame. Override only the callbacks you need; the scene calls them in
/// dependency order declared with <c>[RunAfter]</c> and <c>[RunBefore]</c>.
/// </summary>
/// <example>
/// The following example shows a system that spins every rectangle:
/// <code>
/// [Pausable]
/// public class Spinner : System
/// {
///     public float Speed = 2f;
///
///     public override void FixedUpdate()
///     {
///         float step = Speed * Time.DeltaSeconds;
///         Scene.ECS.Query&lt;Transform2D, Rectangle2D&gt;((int thread, int entity, ref Transform2D transform, ref Rectangle2D rectangle) =>
///             transform.Rotation += step);
///     }
/// }
/// </code>
/// </example>
public abstract class System
{
    /// <summary>The scene this system belongs to. Reach entities through <c>Scene.ECS</c>.</summary>
    public Scene Scene;

    /// <summary>Timing for the current callback; advance by <c>Time.DeltaSeconds</c>.</summary>
    public FrameTime Time;

    /// <summary>This frame's input. Read held keys in <c>FixedUpdate</c> and presses, releases and text in <c>Update</c>.</summary>
    public com.radiant.engine.input.InputFrame Controls => Scene?.Controls ?? com.radiant.engine.input.InputFrame.Empty;

    /// <summary>Draws shapes, sprites, text and shader passes. Null when running headless.</summary>
    public IRenderer Renderer;

    /// <summary>Set to false to skip every callback of this system until it is enabled again.</summary>
    public bool Enabled = true;
    /// <summary>Specific system instances to run after, for scenes that contain several instances of the same type.</summary>
    public System[] After = Array.Empty<System>();

    internal PauseGroup PauseGroups;

    /// <summary>The layer this system's <c>LateRender</c> draws on. Override to draw overlays or UI on top.</summary>
    public virtual RenderLayer RenderLayer => RenderLayer.Gameplay;

    /// <summary>Runs once when the scene starts. Create render targets and other resources here.</summary>
    public virtual void Initialize() {}

    /// <summary>Runs when the system or scene shuts down. Release what <c>Initialize</c> created.</summary>
    public virtual void Dispose() {}

    /// <summary>Runs every frame. Handle input presses and frame-rate-dependent work here.</summary>
    public virtual void Update() {}

    /// <summary>Runs 64 times per second. Put movement, physics and other simulation here.</summary>
    public virtual void FixedUpdate() {}

    /// <summary>Runs every frame to produce images other systems may read, such as lighting buffers.</summary>
    public virtual void Render() {}

    /// <summary>Runs every frame after all <c>Render</c> calls, ordered by <c>RenderLayer</c>. Draw overlays and UI here.</summary>
    public virtual void LateRender() {}

    /// <summary>Runs after the window size changes. Recreate size-dependent render targets here.</summary>
    public virtual void OnResize() {}
}
