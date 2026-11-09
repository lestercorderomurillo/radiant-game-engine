using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Geometry-guided temporal reconstruction for HRC lighting. Call ResetHistory on scene cuts.</summary>
[RunAfter(typeof(Geometry), typeof(HRCGI), typeof(ColorManagement))]
public class UDR4 : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    /// <summary>Disable automatic presentation when an explicit presentation stage consumes GetOutput.</summary>
    public bool Present = true;
    /// <summary>Spatial fallback for sources without HRC geometry and motion.</summary>
    public bool Temporal = true;
    /// <summary>Connect HRC buffers only when the input is actually derived from HRC.</summary>
    public bool UseHrcGeometry = true;
    public float Sharpness = 0.25f;
    public int FramesToAccumulate = 8;
    public int HistoryFrames { get; private set; }

    private Func<Texture2D> InputSource;
    private Geometry Geometry;
    private HRCGI Lighting;
    private RenderTarget2D OutputTexture;
    private RenderTarget2D HistoryRead;
    private RenderTarget2D HistoryWrite;
    private RenderTarget2D GeometryHistory;
    private Texture2D PreviousInput;
    private Vector2 InputSize;
    private Vector2 OutputSize;
    private Matrix PreviousView;
    private bool Initialized;
    private bool OutputReady;
    private int JitterIndex;

    /// <inheritdoc/>
    public override void Initialize()
    {
        if (Initialized) return;

        Initialized = true;
        Geometry = UseHrcGeometry ? Scene.ECS.GetSystem<Geometry>() : null;
        Lighting = UseHrcGeometry ? Scene.ECS.GetSystem<HRCGI>() : null;
        if (Geometry != null) Geometry.TemporalMotionEnabled = Temporal && UDRQuality.ScaleFactor != 100;
        UDRQuality.Changed += OnQualityChanged;
        Renderer.RenderScaleChanged += OnScaleChanged;
        Renderer.RenderScale = UDRQuality.ScaleNormalized;
        ResetHistory();

        Inspector.CreateWindow("udr4", "UDR4", Visible: false);
        Inspector.AddLabel("udr4", "resolution", "...");
        Inspector.AddDropdown("udr4", "qualityDrop", "Quality", UDRQuality.Names, UDRQuality.Index, UDRQuality.Set);
        Inspector.AddSlider("udr4", "sharpness", "Sharpness", 0f, 1f, Sharpness, value => Sharpness = value);
        Inspector.AddSlider("udr4", "frames", "History Frames", 1, 16, FramesToAccumulate,
            value => { FramesToAccumulate = (int)value; ResetHistory(); });
    }

    /// <summary>Sets the live image provider consumed by this stage.</summary>
    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
        ResetHistory();
    }

    /// <summary>Discard history after a teleport, level change, or discontinuous input change.</summary>
    public void ResetHistory()
    {
        HistoryFrames = 0;
        JitterIndex = 0;
        OutputReady = false;
        UpdateJitter();
    }

    /// <summary>Advances the subpixel sampling offset used for temporal reconstruction.</summary>
    private void UpdateJitter()
    {
        if (!Initialized) return;

        if (Geometry != null) Geometry.TemporalMotionEnabled = Temporal && UDRQuality.ScaleFactor != 100;

        if (Lighting == null) return;

        Lighting.OutputJitter = !Temporal || UDRQuality.ScaleFactor == 100 ? Vector2.Zero : JitterSample(JitterIndex);
    }

    /// <summary>Eight deterministic subpixel samples in input-texel units.</summary>
    public static Vector2 JitterSample(int index)
    {
        int sample = ((index % 8) + 8) % 8 + 1;
        return new Vector2(Halton(sample, 2) - 0.5f, Halton(sample, 3) - 0.5f);
    }

    /// <summary>Computes a sample of the low-discrepancy Halton sequence in the given base.</summary>
    private static float Halton(int index, int radix)
    {
        float result = 0, fraction = 1;

        while (index > 0)
        {
            fraction /= radix;
            result += fraction * (index % radix);
            index /= radix;
        }

        return result;
    }

    /// <summary>Applies the selected reconstruction quality and refreshes dependent resources.</summary>
    private void OnQualityChanged(int index)
    {
        Renderer.RenderScale = UDRQuality.ScaleNormalized;
        ResetHistory();
        Inspector.SetDropdownValue("udr4", "qualityDrop", index);
    }

    /// <summary>Invalidates reconstruction resources after a render-scale change.</summary>
    private void OnScaleChanged(float _) => ResetHistory();

    /// <inheritdoc/>
    public override void Update()
    {
        var input = InputSource?.Invoke();

        if (input == null)
        {
            ResetHistory();
            return;
        }
        if (UDRQuality.ScaleFactor == 100)
        {
            ResetHistory();
            return;
        }

        EnsureTargets();

        var size = new Vector2(input.Width, input.Height);
        if (InputSize != size || !ReferenceEquals(PreviousInput, input)) ResetHistory();
        InputSize = size;
        PreviousInput = input;

        var black = Renderer.GetSolidTexture(Color.Transparent);
        Texture2D emission = Geometry?.EmissiveTexture ?? black;
        Texture2D absorption = Geometry?.AbsorptionTexture ?? black;
        Texture2D motion = Geometry?.TemporalMotionTexture ?? black;
        bool hasGeometry = Geometry?.Enabled == true && Geometry.EmissiveTexture != null;
        Vector2 jitter = Lighting?.Enabled == true ? Lighting.LastOutputJitter : Vector2.Zero;

        Inspector.SetLabel("udr4", "resolution", $"{input.Width}x{input.Height} → {(int)OutputSize.X}x{(int)OutputSize.Y}");
        Renderer.Reset().SetShader("UDR/UDR4").SetTechnique("Spatial")
            .Configure(BlendState.Opaque).Configure(SamplerState.LinearClamp).SetTarget(Temporal ? OutputTexture : HistoryWrite)
            .SetParameter("InputTexture", input).SetParameter("InputSize", InputSize)
            .SetParameter("OutputSize", OutputSize).SetParameter("Jitter", jitter)
            .SetParameter("EmissiveTexture", emission).SetParameter("AbsorptionTexture", absorption)
            .SetParameter("HasGeometry", hasGeometry ? 1f : 0f)
            .Draw().Commit().SetTarget(null);

        if (Temporal)
        {
            Renderer.Reset().SetShader("UDR/UDR4").SetTechnique("Temporal")
                .Configure(BlendState.Opaque).Configure(SamplerState.LinearClamp).SetTarget(HistoryWrite)
                .SetParameter("InputTexture", OutputTexture).SetParameter("HistoryTexture", HistoryRead)
                .SetParameter("GeometryHistory", GeometryHistory).SetParameter("MotionTexture", motion)
                .SetParameter("EmissiveTexture", emission).SetParameter("AbsorptionTexture", absorption)
                .SetParameter("HasHistory", HistoryFrames > 0 ? 1f : 0f)
                .SetParameter("HasGeometry", hasGeometry ? 1f : 0f)
                .SetParameter("HasMotion", hasGeometry && Geometry.TemporalMotionTexture != null ? 1f : 0f)
                .SetParameter("MaxHistory", (float)Math.Clamp(FramesToAccumulate, 1, 16))
                .SetParameter("OutputSize", OutputSize)
                .SetParameter("CurrentScreenToWorld", MatrixMath.Invert(Renderer.WorldTransform))
                .SetParameter("PreviousWorldToScreen", PreviousView)
                .Draw().Commit().SetTarget(null);

            Renderer.Reset().SetShader("UDR/UDR4").SetTechnique("StoreGeometry")
                .Configure(BlendState.Opaque).Configure(SamplerState.LinearClamp).SetTarget(GeometryHistory)
                .SetParameter("EmissiveTexture", emission).SetParameter("AbsorptionTexture", absorption)
                .SetParameter("HasGeometry", hasGeometry ? 1f : 0f)
                .Draw().Commit().SetTarget(null);
        }

        // Keep unsharpened history; reuse the spatial target for presentation.
        Renderer.Reset().SetShader("UDR/UDR4").SetTechnique("Sharpen")
            .Configure(BlendState.Opaque).Configure(SamplerState.LinearClamp).SetTarget(OutputTexture)
            .SetParameter("InputTexture", HistoryWrite).SetParameter("OutputSize", OutputSize)
            .SetParameter("Sharpness", Math.Clamp(Sharpness, 0f, 1f))
            .SetParameter("PreserveAlpha", Temporal ? 0f : 1f)
            .Draw().Commit().SetTarget(null);

        if (Temporal) (HistoryRead, HistoryWrite) = (HistoryWrite, HistoryRead);
        PreviousView = Renderer.WorldTransform;
        HistoryFrames = Temporal ? Math.Min(HistoryFrames + 1, 16) : 0;
        OutputReady = true;
        JitterIndex = (JitterIndex + 1) % 8;
        UpdateJitter();
    }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public Texture2D GetOutput() => Enabled && UDRQuality.ScaleFactor != 100 && OutputReady ? OutputTexture : InputSource?.Invoke();

    /// <inheritdoc/>
    public override void Render()
    {
        if (Present && OutputReady) Renderer.Blit(OutputTexture, BlendState.Opaque, SamplerState.LinearClamp);
    }

    /// <summary>Creates or resizes reconstruction targets for the current input and view.</summary>
    private void EnsureTargets()
    {
        if (OutputTexture != null && OutputSize == Renderer.ScreenSize && (HistoryRead != null) == Temporal) return;

        DisposeTargets();
        OutputSize = Renderer.ScreenSize;
        OutputTexture = CreateTarget();
        HistoryWrite = CreateTarget();

        if (Temporal)
        {
            HistoryRead = CreateTarget();
            GeometryHistory = CreateTarget();
        }

        ResetHistory();
    }

    /// <summary>Allocates a reconstruction target through the renderer.</summary>
    private RenderTarget2D CreateTarget() => Renderer.CreateRenderTarget((int)OutputSize.X,
        (int)OutputSize.Y, SurfaceFormat.HalfVector4);

    /// <inheritdoc/>
    public override void OnResize()
    {
        DisposeTargets();
        ResetHistory();
    }

    /// <summary>Releases reconstruction targets and invalidates their history.</summary>
    private void DisposeTargets()
    {
        OutputTexture?.Dispose();
        HistoryRead?.Dispose();
        HistoryWrite?.Dispose();
        GeometryHistory?.Dispose();
        OutputTexture = HistoryRead = HistoryWrite = GeometryHistory = null;
        OutputReady = false;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (!Initialized) return;

        Initialized = false;
        UDRQuality.Changed -= OnQualityChanged;
        Renderer.RenderScaleChanged -= OnScaleChanged;
        if (Lighting != null) Lighting.OutputJitter = Vector2.Zero;
        if (Geometry != null) Geometry.TemporalMotionEnabled = false;
        DisposeTargets();
        PreviousInput = null;
        HistoryFrames = 0;
        Inspector.DestroyWindow("udr4");
        Renderer.RenderScale = 1f;
    }
}
