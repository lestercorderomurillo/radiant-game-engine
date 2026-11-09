using System;
using com.radiant.engine.runtime;

namespace com.radiant.engine.core;

/// <summary>Orders a system after the specified system types.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RunAfterAttribute : Attribute
{
    public Type[] SystemTypes { get; }

    /// <summary>Records the system types that must run before the annotated system.</summary>
    public RunAfterAttribute(params Type[] systemTypes) => SystemTypes = systemTypes;
}

/// <summary>Orders a system before the specified system types.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RunBeforeAttribute : Attribute
{
    public Type[] SystemTypes { get; }

    /// <summary>Records the system types that must run after the annotated system.</summary>
    public RunBeforeAttribute(params Type[] systemTypes) => SystemTypes = systemTypes;
}

/// <summary>Independent gameplay and animation pause flags.</summary>
[Flags]
public enum PauseGroup : byte
{
    None = 0,
    Gameplay = 1,
    Animation = 2
}

/// <summary>
/// Marks a system as pausable. Specify which pause groups affect this system.
/// Systems with Gameplay are skipped when ECS.GameplayPaused is true.
/// Systems with Animation are skipped when ECS.AnimationPaused is true.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class PausableAttribute : Attribute
{
    public PauseGroup Groups { get; }

    /// <summary>Sets the pause groups that suppress this system's callbacks.</summary>
    public PausableAttribute() => Groups = PauseGroup.Gameplay;

    /// <summary>Sets the pause groups that suppress this system's callbacks.</summary>
    public PausableAttribute(PauseGroup groups) => Groups = groups;
}

/// <summary>
/// Marks a system as core infrastructure that cannot be disabled via the Systems Inspector.
/// Core systems are hidden from the Systems Inspector list entirely.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CoreSystemAttribute : Attribute { }

/// <summary>Assigns categories used to group systems in the inspector.</summary>
[AttributeUsage(AttributeTargets.Class)]
public class SystemTagAttribute : Attribute
{
    public string[] Tags { get; }

    /// <summary>Stores the inspector category labels for a system type.</summary>
    public SystemTagAttribute(params string[] tags) => Tags = tags;
}

/// <summary>Coarse drawing order from world content to user interface.</summary>
public enum RenderLayer : byte { World = 0, Gameplay = 1, Overlay = 2, UI = 3 }

/// <summary>Base for an ECS system with ordered update, rendering, and resource callbacks.</summary>
public abstract class System
{
    public Scene Scene;

    public FrameTime Time;

    /// <summary>Shared scene input; read held state in FixedUpdate and edge/text events in Update.</summary>
    public com.radiant.engine.input.InputFrame Controls => Scene?.Controls ?? com.radiant.engine.input.InputFrame.Empty;

    public Renderer Renderer;

    public bool Enabled = true;
    /// <summary>Dependencies by instance, allowing independent views with the same system types.</summary>
    public System[] After = Array.Empty<System>();

    internal PauseGroup PauseGroups;

    public virtual RenderLayer RenderLayer => RenderLayer.Gameplay;

    /// <summary>Prepares resources after the system is attached to its scene and renderer.</summary>
    public virtual void Initialize() {}

    /// <summary>Releases resources owned by this system.</summary>
    public virtual void Dispose() {}

    /// <summary>Updates frame-dependent system state while enabled and not paused.</summary>
    public virtual void Update() {}

    /// <summary>Advances fixed-timestep simulation while enabled and not paused.</summary>
    public virtual void FixedUpdate() {}

    /// <summary>Submits rendering work in the system's configured render order.</summary>
    public virtual void Render() {}

    /// <summary>Draws overlays after the main system render pass.</summary>
    public virtual void LateRender() {}

    /// <summary>Refreshes resources that depend on the renderer's current view size.</summary>
    public virtual void OnResize() {}
}
