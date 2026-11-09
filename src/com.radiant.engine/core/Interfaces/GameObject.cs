using System;

namespace com.radiant.engine.core;

/// <summary>Lifecycle callbacks for scene initialization, simulation, and drawing.</summary>
public interface IGameObject : IDisposable
{
    /// <summary>Prepares resources before the object begins receiving frame callbacks.</summary>
    void Initialize();

    /// <summary>Advances frame-dependent state using the current frame time.</summary>
    void Update();

    /// <summary>Advances simulation during a fixed-timestep tick.</summary>
    void FixedUpdate();

    /// <summary>Submits the object's main rendering work.</summary>
    void Render();

    /// <summary>Draws overlays after the main rendering phase.</summary>
    void LateRender();
}
