using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>Named live outputs. Providers survive resize; missing optional buffers return null.</summary>
public interface IRenderSource
{
    Texture2D GetOutput();
    IReadOnlyDictionary<string, Func<Texture2D>> Buffers { get; }
}

public abstract class ImageSystem : System, IRenderSource
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    public Func<Texture2D> Input;
    protected RenderTarget2D Output;
    public IReadOnlyDictionary<string, Func<Texture2D>> Buffers => BufferProviders;
    protected readonly Dictionary<string, Func<Texture2D>> BufferProviders = new();
    protected ImageSystem() { BufferProviders["Input"] = () => Input?.Invoke(); BufferProviders["Output"] = GetOutput; }
    public virtual Texture2D GetOutput() => Enabled ? Output : Input?.Invoke();
    protected void EnsureOutput(Texture2D input, SurfaceFormat? format = null)
    {
        var requested = format ?? input.Format;
        if (Output?.Width == input.Width && Output.Height == input.Height && Output.Format == requested) return;
        Renderer.SetTarget(null);
        Output?.Dispose();
        Output = Renderer.CreateRenderTarget(input.Width, input.Height, requested);
    }
    public override void Dispose() { Output?.Dispose(); Output = null; }
}
