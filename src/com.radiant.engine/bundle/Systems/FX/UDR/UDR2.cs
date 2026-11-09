using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Temporal reconstruction stage that accumulates reduced-resolution HRC lighting.</summary>
public class UDR2 : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    // Spatial parameters
    public float Sharpness = 0.5f;
    public bool EdgeCorrection = true;
    public bool DebugRays = false;

    // Temporal parameters (adjustable)
    public int FramesToAccumulate = 6;         // Number of frames to average together (1 = no temporal, 10 = very smooth)

    private const int MaxFrames = 16;

    private RenderTarget2D SpatialTexture;
    private RenderTarget2D AccumulationTexture;  // Running sum of frames
    private RenderTarget2D SmoothTexture;
    private Vector2 OutputSize;

    private Func<Texture2D> InputSource;
    private Geometry Geometry;

    private int FrameIndex = 0;

    /// <inheritdoc/>
    public override void Initialize()
    {
        Geometry = Scene.ECS.GetSystem<Geometry>();
        OutputSize = Renderer.ScreenSize;
        CreateRenderTargets();
        ApplyRenderScale();
        FrameIndex = 0;

        if (Geometry != null) Geometry.EnableSDF = true;

        Inspector.CreateWindow("udr2", "UDR2", Visible: false);
        Inspector.AddLabel("udr2", "quality", "...");
        Inspector.AddLabel("udr2", "input", "...");
        Inspector.AddLabel("udr2", "output", "...");
        Inspector.AddDropdown("udr2", "qualityDrop", "Quality", UDRQuality.Names, UDRQuality.Index, UDRQuality.Set);
        UDRQuality.Changed += OnQualityChanged;
        Inspector.AddSlider("udr2", "sharpness", "Sharpness", 0f, 2f, Sharpness, V => Sharpness = V);
        Inspector.AddSlider("udr2", "frames", "Frames to Accumulate", 1, 16, FramesToAccumulate, V => { FramesToAccumulate = (int)V; FrameIndex = 0; });
        Inspector.AddToggle("udr2", "edgeCorr", "Detail Reconstruction", EdgeCorrection, V => EdgeCorrection = V);
        Inspector.AddToggle("udr2", "debugRays", "Debug Edges", DebugRays, V => DebugRays = V);
    }

    /// <summary>Sets the live image provider consumed by this stage.</summary>
    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
        ResetAccumulation();
    }

    /// <summary>Invalidates progressive samples so lighting restarts from the current scene.</summary>
    public void ResetAccumulation()
    {
        FrameIndex = 0;

        // Clear the accumulation buffer to avoid showing stale history
        if (AccumulationTexture != null)
        {
            Renderer.SetTarget(AccumulationTexture).Clear(Color.Black);
            Renderer.SetTarget(null);
        }
    }

    /// <summary>Allocates the image targets required by this rendering stage.</summary>
    private void CreateRenderTargets()
    {
        SpatialTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);

        AccumulationTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);

        SmoothTexture = Renderer.CreateRenderTarget(
            (int)OutputSize.X, (int)OutputSize.Y, SurfaceFormat.HalfVector4);
    }

    /// <summary>Applies the selected reconstruction quality and refreshes dependent resources.</summary>
    private void OnQualityChanged(int index)
    {
        ApplyRenderScale();
        ResetAccumulation();
        Inspector.SetDropdownValue("udr2", "qualityDrop", index);
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

        Inspector.SetLabel("udr2", "quality", $"Quality: {UDRQuality.Names[UDRQuality.Index]} ({UDRQuality.ScaleNormalized:P0})");
        Inspector.SetLabel("udr2", "input", $"Input Size: {input.Width}x{input.Height}");
        Inspector.SetLabel("udr2", "output", $"Output Size: {OutputSize.X}x{OutputSize.Y}");

        if (UDRQuality.ScaleFactor == 100) return;

        // Pass 1: Spatial upscaling (Lanczos + edge refinement)
        Renderer
            .Reset()
            .SetShader("UDR/UDR2")
            .SetTechnique("Spatial")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(SpatialTexture)
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

        // Pass 2: Temporal accumulation (running average)
        int effectiveFrames = Math.Min(FrameIndex + 1, FramesToAccumulate);
        float currentWeight = 1.0f / effectiveFrames;

        Renderer
            .Reset()
            .SetShader("UDR/UDR2")
            .SetTechnique("Temporal")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(SmoothTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", SpatialTexture)
            .SetParameter("EmissiveTexture", Geometry?.EmissiveTexture)
            .SetParameter("SDFTexture", Geometry?.SDFTexture)
            .SetParameter("HistoryTexture", AccumulationTexture)
            .SetParameter("AbsorptionTexture", Geometry?.AbsorptionTexture)
            .SetParameter("OutputSize", OutputSize)
            .SetParameter("CurrentWeight", currentWeight)
            .SetParameter("FrameCount", (float)effectiveFrames)
            .Draw()
            .Commit()
            .SetTarget(null);

        // Copy result to accumulation buffer for next frame
        Renderer
            .Reset()
            .SetShader("UDR/UDR2")
            .SetTechnique("Copy")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(AccumulationTexture)
            .Clear(Color.Black)
            .SetParameter("InputTexture", SmoothTexture)
            .Draw()
            .Commit()
            .SetTarget(null);

        FrameIndex++;
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

        DisposeRenderTargets();
        OutputSize = newSize;
        CreateRenderTargets();
        FrameIndex = 0;  // Reset accumulation on resize
    }

    /// <summary>Releases render targets owned by this stage.</summary>
    private void DisposeRenderTargets()
    {
        SpatialTexture?.Dispose();
        AccumulationTexture?.Dispose();
        SmoothTexture?.Dispose();
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        UDRQuality.Changed -= OnQualityChanged;

        var geometry = Scene?.ECS?.GetSystem<Geometry>();
        if (geometry != null) geometry.EnableSDF = false;
        Inspector.DestroyWindow("udr2");
        Renderer.RenderScale = 1.0f;

        DisposeRenderTargets();
        SpatialTexture = null;
        AccumulationTexture = null;
        SmoothTexture = null;
    }
}
