using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Reconstructs a color image with Lanczos filtering, edge refinement and temporal accumulation.</summary>
/// <remarks>Uses optional matching 2D geometry guidance, then sharpens the reconstructed result. Use Upscaling.ResetHistory when hosted by the shared stage. Use <see cref="UDRQuality"/> for resolution presets.</remarks>
/// <seealso cref="Upscaling"/>
public class UDR3 : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    /// <summary>Uses matching 2D Geometry buffers when available. Set false before initialization for image-only reconstruction.</summary>
    public bool UseGeometryGuidance = true;
    /// <summary>Controls final edge contrast; higher values produce stronger sharpening.</summary>
    public float Sharpness = 0.5f;
    public bool EdgeCorrection = true;
    /// <summary>Target temporal history length in frames. Longer history smooths the image but responds more slowly to change.</summary>
    public int FramesToAccumulate = 6;

    private int DebugEdges = 0;

    private RenderTarget2D SpatialTexture;
    private RenderTarget2D EdgeTexture;
    private RenderTarget2D TemporalTexture;
    private RenderTarget2D LastFrameTexture;
    private Vector2 OutputSize;

    private Func<Texture2D> InputSource;
    private Geometry Geometry;

    private int FrameIndex = 0;

    /// <inheritdoc/>
    public override void Initialize()
    {
        Geometry = UseGeometryGuidance ? Scene.ECS.GetSystem<Geometry>() : null;

        OutputSize = Renderer.ScreenSize;
        CreateRenderTargets();
        ApplyRenderScale();
        FrameIndex = 0;

        Inspector.CreateWindow("udr3", "UDR3", Visible: false);
        Inspector.AddLabel("udr3", "quality", "...");
        Inspector.AddLabel("udr3", "input", "...");
        Inspector.AddLabel("udr3", "output", "...");
        Inspector.AddDropdown("udr3", "qualityDrop", "Quality", UDRQuality.Names, UDRQuality.Index, UDRQuality.Set);
        UDRQuality.Changed += OnQualityChanged;
        Inspector.AddSlider("udr3", "sharpness", "Sharpness", 0f, 2f, Sharpness, V => Sharpness = V);
        Inspector.AddSlider("udr3", "frames", "Frames to Accumulate", 1, 16, FramesToAccumulate, V => { FramesToAccumulate = (int)V; FrameIndex = 0; });
        Inspector.AddToggle("udr3", "edgeCorr", "Detail Reconstruction", EdgeCorrection, V => EdgeCorrection = V);
        Inspector.AddToggle("udr3", "debugEdges", "Debug Edges", false, (enabled) => DebugEdges = enabled ? 1 : 0);
    }

    /// <summary>Connects the image provider evaluated by the reconstruction stage.</summary>
    /// <param name="source">Provider for the current input image; connect it once rather than reassigning it every frame.</param>
    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
        FrameIndex = 0;
    }

    /// <summary>Allocates the image targets required by this rendering stage.</summary>
    private void CreateRenderTargets()
    {
        SpatialTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);

        EdgeTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);

        TemporalTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);

        LastFrameTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);
    }

    /// <summary>Applies the selected reconstruction quality and refreshes dependent resources.</summary>
    private void OnQualityChanged(int index)
    {
        ApplyRenderScale();
        FrameIndex = 0;
        Inspector.SetDropdownValue("udr3", "qualityDrop", index);
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

        Inspector.SetLabel("udr3", "quality", $"Quality: {UDRQuality.Names[UDRQuality.Index]} ({UDRQuality.ScaleNormalized:P0})");
        Inspector.SetLabel("udr3", "input", $"Input Size: {input.Width}x{input.Height}");
        Inspector.SetLabel("udr3", "output", $"Output Size: {OutputSize.X}x{OutputSize.Y}");

        if (UDRQuality.ScaleFactor == 100) return;

        bool hasGeometry = Geometry is { Enabled: true, EmissiveTexture: not null, AbsorptionTexture: not null };
        var black = Renderer.GetSolidTexture(Color.Transparent);

        // Pass 1: Spatial (Lanczos upsampling)
        Renderer
            .Reset()
            .SetShader("UDR/UDR3")
            .SetTechnique("UDR3_Stage1")
            .Configure(BlendState.Opaque)
            .Configure(SamplerState.PointClamp)
            .SetTarget(SpatialTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", input)
            .SetParameter("InputSize", inputSize)
            .Draw()
            .Commit()
            .SetTarget(null);

        if (hasGeometry)
        {
            // Pass 2: Geometry-guided edge reconstruction
            Renderer
                .Reset()
                .SetShader("UDR/UDR3")
                .SetTechnique("UDR3_Stage2")
                .Configure(BlendState.Opaque)
                .Configure(SamplerState.LinearClamp)
                .SetTarget(EdgeTexture)
                .Clear(Color.Black)
                .SetParameter("InputTexture", SpatialTexture)
                .SetParameter("EmissiveTexture", Geometry?.EmissiveTexture ?? black)
                .SetParameter("SDFTexture", Geometry?.SDFTexture ?? black)
                .SetParameter("MotionVectorTexture", Geometry?.MotionVectorTexture ?? black)
                .SetParameter("AbsorptionTexture", Geometry?.AbsorptionTexture ?? black)
                .SetParameter("InputSize", inputSize)
                .SetParameter("OutputSize", OutputSize)
                .SetParameter("EdgeCorrection", EdgeCorrection && hasGeometry ? 1f : 0f)
                .SetParameter("DebugEdges", (float)DebugEdges)
                .Draw()
                .Commit()
                .SetTarget(null);

            // Pass 3: Temporal accumulation
            int effectiveFrames = Math.Min(FrameIndex + 1, Math.Clamp(FramesToAccumulate, 1, 16));
            float currentWeight = 1.0f / effectiveFrames;

            Renderer
                .Reset()
                .SetShader("UDR/UDR3")
                .SetTechnique("UDR3_Stage3")
                .Configure(BlendState.Opaque)
                .Configure(SamplerState.LinearClamp)
                .SetTarget(TemporalTexture)
                .Clear(Color.Black)
                .SetParameter("InputTexture", EdgeTexture)
                .SetParameter("LastFrame", LastFrameTexture)
                .SetParameter("MotionVectorTexture", Geometry?.MotionVectorTexture ?? black)
                .SetParameter("OutputSize", OutputSize)
                .SetParameter("CurrentWeight", currentWeight)
                .Draw()
                .Commit()
                .SetTarget(null);

            // Pass 4: Copy to LastFrame for next frame (unsharpened)
            Renderer
                .Reset()
                .SetShader("UDR/UDR3")
                .SetTechnique("UDR3_Stage4")
                .Configure(BlendState.Opaque)
                .Configure(SamplerState.LinearClamp)
                .SetTarget(LastFrameTexture)
                .Clear(Color.Black)
                .SetParameter("InputTexture", TemporalTexture)
                .Draw()
                .Commit()
                .SetTarget(null);
        }
        else FrameIndex = 0;

        // Pass 5: RCAS sharpening (reuse EdgeTexture for output)
        Renderer
            .Reset()
            .SetShader("UDR/UDR3")
            .SetTechnique("UDR3_Stage5")
            .Configure(BlendState.Opaque)
            .Configure(SamplerState.PointClamp)
            .SetTarget(EdgeTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", hasGeometry ? TemporalTexture : SpatialTexture)
            .SetParameter("OutputSize", OutputSize)
            .SetParameter("Sharpness", Sharpness)
            .SetParameter("PreserveAlpha", hasGeometry ? 0f : 1f)
            .Draw()
            .Commit()
            .SetTarget(null);

        if (hasGeometry) FrameIndex = Math.Min(FrameIndex + 1, 16);
    }

    /// <inheritdoc/>
    public override void Render()
    {
        if (InputSource == null || UDRQuality.ScaleFactor == 100) return;

        Renderer.Blit(EdgeTexture, BlendState.AlphaBlend, SamplerState.LinearClamp);
    }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public RenderTarget2D GetOutput() => EdgeTexture;

    /// <inheritdoc/>
    public override void OnResize()
    {
        Vector2 newSize = Renderer.ScreenSize;

        if (OutputSize == newSize) return;

        DisposeRenderTargets();
        OutputSize = newSize;
        CreateRenderTargets();
        FrameIndex = 0;
    }

    /// <summary>Releases render targets owned by this stage.</summary>
    private void DisposeRenderTargets()
    {
        SpatialTexture?.Dispose();
        EdgeTexture?.Dispose();
        TemporalTexture?.Dispose();
        LastFrameTexture?.Dispose();
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        UDRQuality.Changed -= OnQualityChanged;
        Inspector.DestroyWindow("udr3");
        Renderer.RenderScale = 1.0f;

        DisposeRenderTargets();
        SpatialTexture = null;
        EdgeTexture = null;
        TemporalTexture = null;
        LastFrameTexture = null;
    }
}
