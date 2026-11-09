using System;
using com.radiant.engine.core;

namespace com.radiant.engine.graphics;

/// <summary>Render-thread submission boundary. Consume the array synchronously; do not retain it.</summary>
public interface IShapeBatchBackend : IDisposable
{
    float LastSetDataMs { get; }
    float LastDrawMs { get; }
    /// <summary>Draws the first count shapes. A zero count still applies target binding and optional clear.</summary>
    void Submit(Shape[] shapes, int count, Matrix viewProjection, RenderTarget2D target,
        Color? clearColor, BlendState blendState, Effect shader, string technique);
}
