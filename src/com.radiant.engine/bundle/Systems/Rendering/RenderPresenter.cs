using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Optional presentation. Independent instances may composite multiple views or target textures.</summary>
[SystemTag("Rendering")]
public class RenderPresenter : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    public Func<Texture2D> Input;
    public Func<RenderTarget2D> Target;
    public Rectangle? Destination;
    public Color? ClearColor;
    public BlendState Blend = BlendState.Opaque;

    /// <inheritdoc/>
    public override void Render()
    {
        var input = Input?.Invoke();

        if (input == null) return;

        var target = Target?.Invoke();

        if (ReferenceEquals(input, target ?? Renderer.SceneRT)) throw new InvalidOperationException("Presentation cannot sample its render destination.");

        Renderer.Reset().SetTarget(target);
        if (ClearColor.HasValue) Renderer.Clear(ClearColor);
        Renderer.Configure(Blend).Configure(SamplerState.LinearClamp).DrawTexture(input, Destination ?? Renderer.ViewportBounds, Color.White).Commit().SetTarget(null);
    }
}
