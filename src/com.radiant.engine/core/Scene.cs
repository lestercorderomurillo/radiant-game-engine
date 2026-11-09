using System;
using com.radiant.engine.runtime;

namespace com.radiant.engine.core;

/// <summary>Owns a scene's entities and systems and exposes its lifecycle callbacks.</summary>
public class Scene : IGameObject
{
    public int Id { get; private set; }

    public ECS ECS { get; private set; }

    public FrameTime Time { get; set; }

    /// <summary>Input captured once for the current variable update by the runtime.</summary>
    public com.radiant.engine.input.InputFrame Controls { get; internal set; } = com.radiant.engine.input.InputFrame.Empty;

    public float DeltaTime { get; set; }

    public Renderer Renderer { get; set; }

    /// <summary>Creates the ECS bound to this scene and its renderer.</summary>
    public void Initialize()
    {
        ECS = new ECS(this, Renderer);
    }

    /// <summary>Releases the scene's ECS and system resources.</summary>
    public void Dispose()
    {
        ECS?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Registers scene systems; call the base implementation to initialize the ECS.</summary>
    public virtual void SetupECS()
    {
        ECS.Initialize();
    }

    /// <summary>Creates scene entities after system setup has completed.</summary>
    public virtual void SetupScene()
    {
    }

    /// <summary>Updates scene-specific state before ECS systems receive their frame update.</summary>
    public virtual void Update() { }

    /// <summary>Updates scene-specific simulation before the ECS fixed-timestep pass.</summary>
    public virtual void FixedUpdate() { }

    /// <summary>Draws scene content before the ECS render pass.</summary>
    public virtual void Render() { }

    /// <summary>Draws scene overlays before the ECS late-render pass.</summary>
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
