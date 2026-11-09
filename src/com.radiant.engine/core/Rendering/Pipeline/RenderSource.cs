using System;
using System.Collections.Generic;

namespace com.radiant.engine.core;

/// <summary>Named live outputs. Providers survive resize; missing optional buffers return null.</summary>
public interface IRenderSource
{
    /// <summary>Returns the current primary image, or null when no image is available.</summary>
    Texture2D GetOutput();

    IReadOnlyDictionary<string, Func<Texture2D>> Buffers { get; }
}

/// <summary>Base image-processing stage that exposes its output and named diagnostic buffers.</summary>
public abstract class ImageSystem : System, IRenderSource
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    public Func<Texture2D> Input;

    protected RenderTarget2D Output;

    public IReadOnlyDictionary<string, Func<Texture2D>> Buffers => BufferProviders;

    protected readonly Dictionary<string, Func<Texture2D>> BufferProviders = new();

    /// <summary>Exposes live input and output providers for buffer inspection.</summary>
    protected ImageSystem() { BufferProviders["Input"] = () => Input?.Invoke(); BufferProviders["Output"] = GetOutput; }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public virtual Texture2D GetOutput() => Enabled ? Output : Input?.Invoke();

    /// <summary>Allocates an output target matching the input dimensions and requested format.</summary>
    protected void EnsureOutput(Texture2D input, SurfaceFormat? format = null)
    {
        var requested = format ?? input.Format;

        if (Output?.Width == input.Width && Output.Height == input.Height && Output.Format == requested) return;

        Renderer.SetTarget(null);
        Output?.Dispose();
        Output = Renderer.CreateRenderTarget(input.Width, input.Height, requested);
    }

    /// <inheritdoc/>
    public override void Dispose() { Output?.Dispose(); Output = null; }
}
