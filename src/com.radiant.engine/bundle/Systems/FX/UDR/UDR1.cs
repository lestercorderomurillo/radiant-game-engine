using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Reconstructs HRC lighting spatially, with geometry-guided edge refinement and sharpening.</summary>
/// <remarks>Uses only the current image; it does not accumulate previous frames. Requires the matching HRC geometry buffers. Use <see cref="UDRQuality"/> for resolution presets.</remarks>
/// <seealso cref="Upscaling"/>
public class UDR1 : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    /// <summary>Controls final edge contrast; higher values produce stronger sharpening.</summary>
    public float Sharpness = 0.5f;
    public bool EdgeCorrection = true;
    public bool DebugRays = false;

    private RenderTarget2D OutputTexture;
    private RenderTarget2D SmoothTexture;
    private Vector2 OutputSize;

    private Func<Texture2D> InputSource;
    private Geometry Geometry;

    /// <inheritdoc/>
    public override void Initialize()
    {
        Geometry = Scene.ECS.GetSystem<Geometry>();
        OutputSize = Renderer.ScreenSize;
        CreateRenderTargets();
        ApplyRenderScale();

        Inspector.CreateWindow("udr1", "UDR1", Visible: false);
        Inspector.AddLabel("udr1", "quality", "...");
        Inspector.AddLabel("udr1", "input", "...");
        Inspector.AddLabel("udr1", "output", "...");
        Inspector.AddDropdown("udr1", "qualityDrop", "Quality", UDRQuality.Names, UDRQuality.Index, UDRQuality.Set);
        UDRQuality.Changed += OnQualityChanged;
        Inspector.AddSlider("udr1", "sharpness", "Sharpness", 0f, 2f, Sharpness, V => Sharpness = V);
        Inspector.AddToggle("udr1", "edgeCorr", "Detail Reconstruction", EdgeCorrection, V => EdgeCorrection = V);
        Inspector.AddToggle("udr1", "debugRays", "Debug Edges", DebugRays, V => DebugRays = V);
    }

    /// <summary>Connects the image provider evaluated by the reconstruction stage.</summary>
    /// <param name="source">Provider for the current input image; connect it once rather than reassigning it every frame.</param>
    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
    }

    /// <summary>Allocates the image targets required by this rendering stage.</summary>
    private void CreateRenderTargets()
    {
        OutputTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);

        SmoothTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);
    }

    /// <summary>Applies the selected reconstruction quality and refreshes dependent resources.</summary>
    private void OnQualityChanged(int index)
    {
        ApplyRenderScale();
        Inspector.SetDropdownValue("udr1", "qualityDrop", index);
    }

    /// <summary>Updates the renderer scale to match the selected reconstruction quality.</summary>
    private void ApplyRenderScale()
    {
        Renderer.RenderScale = UDRQuality.ScaleNormalized;
    }

    /// <inheritdoc/>
    public override void Update()
    {
        if (InputSource == null) return;

        var input = InputSource();

        if (input == null) return;

        Vector2 inputSize = new Vector2(input.Width, input.Height);

        Inspector.SetLabel("udr1", "quality", $"Quality: {UDRQuality.Names[UDRQuality.Index]} ({UDRQuality.ScaleNormalized:P0})");
        Inspector.SetLabel("udr1", "input", $"Input Size: {input.Width}x{input.Height}");
        Inspector.SetLabel("udr1", "output", $"Output Size: {OutputSize.X}x{OutputSize.Y}");

        if (UDRQuality.ScaleFactor == 100) return;

        Renderer
            .Reset()
            .SetShader("UDR/UDR1")
            .SetTechnique("Upscale")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(OutputTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", input)
            .SetParameter("EmissiveTexture", Geometry?.EmissiveTexture)
            .SetParameter("SDFTexture", Geometry?.SDFTexture)
            .SetParameter("AbsorptionTexture", Geometry?.AbsorptionTexture)
            .SetParameter("InputSize", inputSize)
            .SetParameter("OutputSize", OutputSize)
            .SetParameter("Sharpness", Sharpness)
            .SetParameter("EdgeCorrection", EdgeCorrection ? 1f : 0f)
            .SetParameter("DebugRays", DebugRays ? 1f : 0f)
            .Draw()
            .Commit()
            .SetTarget(null);

        // Edge smoothing pass (always on)
        Renderer
            .Reset()
            .SetShader("UDR/UDR1")
            .SetTechnique("EdgeSmooth")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(SmoothTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", OutputTexture)
            .SetParameter("OutputSize", OutputSize)
            .Draw()
            .Commit()
            .SetTarget(null);
    }

    /// <inheritdoc/>
    public override void Render()
    {
        if (InputSource == null || UDRQuality.ScaleFactor == 100) return;

        Renderer.Blit(SmoothTexture, BlendState.AlphaBlend, SamplerState.LinearClamp);
    }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public RenderTarget2D GetOutput() => SmoothTexture;

    /// <inheritdoc/>
    public override void OnResize()
    {
        Vector2 newSize = Renderer.ScreenSize;

        if (OutputSize == newSize) return;

        OutputTexture?.Dispose();
        SmoothTexture?.Dispose();
        OutputSize = newSize;
        CreateRenderTargets();
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        UDRQuality.Changed -= OnQualityChanged;
        Inspector.DestroyWindow("udr1");
        Renderer.RenderScale = 1.0f;

        OutputTexture?.Dispose();
        OutputTexture = null;

        SmoothTexture?.Dispose();
        SmoothTexture = null;
    }
}
