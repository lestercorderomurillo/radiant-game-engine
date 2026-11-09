using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

/// <summary>
/// Holographic Radiance Cascades - 2D Global Illumination System
/// Based on the paper by Rouli Freeman (arXiv:2505.02041)
///
/// Uses MRT with separate RGB textures for radiance and transmittance (stained glass support).
/// Single set of cascade surfaces reused for each frustum (memory efficient).
/// </summary>
public class HRCGI : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;

    private const int FrustumCount = 4;
    private const int MaxCascades = 11;
    private static readonly int[] ProbeScales = [4, 3, 2, 1];
    private static readonly string[] QualityNames = ["Performance", "Balanced", "Ultra", "Native"];
    private int ProbeScaleIndex = 2;
    private int ProbeScale => ProbeScales[ProbeScaleIndex];
    private int CascadeCount;

    private Geometry Geometry;

    /// <summary>Subpixel offset in output texels when sampling the radiance field. Zero disables jitter.</summary>
    public Vector2 OutputJitter { get; set; }
    public Vector2 LastOutputJitter { get; private set; }

    // Extensions need both radiance and RGB transmittance. Descending merges
    // need only radiance from the next cascade, so two targets suffice.
    private RenderTarget2D[] VraysRadiance;
    private RenderTarget2D[] VraysTransmittance;
    private RenderTarget2D[] MergeRadiance;

    // Per-frustum output + final composited result
    private RenderTarget2D[] FrustumRadiance;
    private RenderTarget2D FinalTexture;

    private Vector2 WorldSize;
    private Vector2 ViewSize;
    private float ProbeSpacing;
    private Vector2[] CascadeSizes;
    private Vector2[] MergeSizes;

    // Precomputed frustum transforms
    private static readonly Vector4[] FrustumMatrices = [
        new Vector4(1, 0, 0, 1),
        new Vector4(0, -1, -1, 0),
        new Vector4(-1, 0, 0, -1),
        new Vector4(0, 1, 1, 0)
    ];
    private static readonly Vector2[] FrustumOffsets = [
        new Vector2(0, 0),
        new Vector2(1, 1),
        new Vector2(1, 1),
        new Vector2(0, 0)
    ];

    // Solid textures for default values
    private Texture2D BlackTexture;

    /// <inheritdoc/>
    public override void Initialize()
    {
        Geometry = Scene.ECS.GetSystem<Geometry>();

        WorldSize = new Vector2(Renderer.ScaledHigherPowerOfTwo);
        ViewSize = Renderer.ScaledSize;

        // Create solid textures for defaults
        BlackTexture = Renderer.GetSolidTexture(Color.Black);

        CalculateCascadeSizes();
        CreateRenderTargets();

        Renderer.RenderScaleChanged += OnRenderScaleChanged;

        Inspector.CreateWindow("hrcgi", "Holographic Radiance Cascades", Visible: false);
        Inspector.AddLabel("hrcgi", "world", "World Size: ...");
        Inspector.AddLabel("hrcgi", "cascades", "Number of Cascades: ...");
        Inspector.AddDropdown("hrcgi", "quality", "Quality", QualityNames, ProbeScaleIndex, (index) =>
        {
            ProbeScaleIndex = index;
            DisposeRenderTargets();
            CalculateCascadeSizes();
            CreateRenderTargets();
        });
    }

    /// <summary>Rebuilds lighting resources for the new renderer scale.</summary>
    private void OnRenderScaleChanged(float _) => ResizeTargets();

    /// <summary>Releases and recreates lighting targets at the current view size.</summary>
    private void ResizeTargets()
    {
        int newSize = Renderer.ScaledHigherPowerOfTwo;

        if ((int)WorldSize.X == newSize && ViewSize == Renderer.ScaledSize) return;

        DisposeRenderTargets();
        WorldSize = new Vector2(newSize);
        ViewSize = Renderer.ScaledSize;
        CalculateCascadeSizes();
        CreateRenderTargets();
    }

    /// <summary>Computes the resolution and coverage of each lighting cascade.</summary>
    private void CalculateCascadeSizes()
    {
        // Use the allocated row count even when the quality divisor does not divide the domain.
        int probeRows = Math.Max(1, (int)WorldSize.Y / ProbeScale);
        ProbeSpacing = WorldSize.Y / probeRows;
        CascadeCount = Math.Min((int)MathF.Ceiling(MathF.Log2(WorldSize.X)), MaxCascades);
        CascadeSizes = new Vector2[CascadeCount];
        MergeSizes = new Vector2[CascadeCount];

        for (int cascade = 0; cascade < CascadeCount; cascade++)
        {
            float interval = MathF.Pow(2, cascade);
            float virtualRays = interval + 1;
            int numProbes = (int)MathF.Floor(WorldSize.X / interval);

            CascadeSizes[cascade] = new Vector2(numProbes * virtualRays, probeRows);
            MergeSizes[cascade] = new Vector2(numProbes * interval, probeRows);
        }
    }

    /// <summary>Allocates the image targets required by this rendering stage.</summary>
    private void CreateRenderTargets()
    {
        var cascadeFormat = SurfaceFormat.HalfVector4;
        var finalFormat = SurfaceFormat.HalfVector4;

        // Paired cascade surfaces (reused for each frustum)
        VraysRadiance = new RenderTarget2D[CascadeCount];
        VraysTransmittance = new RenderTarget2D[CascadeCount];

        for (int cascade = 0; cascade < CascadeCount; cascade++)
        {
            VraysRadiance[cascade] = Renderer.CreateRenderTarget((int)CascadeSizes[cascade].X, (int)CascadeSizes[cascade].Y, cascadeFormat);
            VraysTransmittance[cascade] = Renderer.CreateRenderTarget((int)CascadeSizes[cascade].X, (int)CascadeSizes[cascade].Y, cascadeFormat);
        }

        // Every merge level has WorldSize.X columns and the same probe rows.
        MergeRadiance = new RenderTarget2D[2];

        for (int i = 0; i < MergeRadiance.Length; i++)
            MergeRadiance[i] = Renderer.CreateRenderTarget((int)MergeSizes[0].X, (int)MergeSizes[0].Y, cascadeFormat);

        FrustumRadiance = new RenderTarget2D[FrustumCount];

        for (int frustum = 0; frustum < FrustumCount; frustum++)
        {
            FrustumRadiance[frustum] = Renderer.CreateRenderTarget((int)WorldSize.X, (int)CascadeSizes[0].Y, cascadeFormat);
        }

        // Cascades use square pixels in a padded square; consumers receive only the viewport.
        FinalTexture = Renderer.CreateRenderTarget((int)ViewSize.X, (int)ViewSize.Y, finalFormat);
    }

    /// <inheritdoc/>
    public override void Update()
    {
        var emissive = Geometry.EmissiveTexture;
        var absorption = Geometry.AbsorptionTexture;

        Renderer.PushTargets();

        // Process each frustum sequentially, reusing cascade textures
        for (int frustum = 0; frustum < FrustumCount; frustum++)
        {
            RenderFrustumSeed(frustum, emissive, absorption);

            for (int cascade = 1; cascade < CascadeCount; cascade++)
                RenderExtensions(cascade);

            for (int cascade = CascadeCount - 1; cascade >= 0; cascade--)
                RenderMerging(cascade);

            CopyToFrustumOutput(frustum);
        }

        Compose();

        Renderer.PopTargets();

        Inspector.SetLabel("hrcgi", "world", $"World Size: {(int)WorldSize.X}");
        Inspector.SetLabel("hrcgi", "cascades", $"Number of Cascades: {CascadeCount}");
    }

    /// <summary>Seeds the first lighting cascade from geometry and emission buffers.</summary>
    private void RenderFrustumSeed(int frustum, Texture2D emissive, Texture2D absorption)
    {
        // Filter geometry at reduced resolution before seeding both radiance and transmittance.
        Renderer
            .SetTargets(VraysRadiance[0], VraysTransmittance[0])
            .Clear(Color.Transparent)
            .Reset()
            .SetShader("HRC/HRC_FrustumSeed")
            .Configure(SamplerState.LinearClamp)
            .SetParameter("Emissivity", emissive)
            .SetParameter("Absorption", absorption)
            .SetParameter("WorldSize", WorldSize)
            .SetParameter("ViewSize", ViewSize)
            .SetParameter("CascadeSize", CascadeSizes[0])
            .SetParameter("FrustumMatrix", FrustumMatrices[frustum])
            .SetParameter("FrustumOffset", FrustumOffsets[frustum])
            .SetParameter("ProbeScale", ProbeSpacing)
            .Draw()
            .Commit();
    }

    /// <summary>Extends lighting probes across each cascade's frustum.</summary>
    private void RenderExtensions(int cascade)
    {
        // MRT: output to both radiance and transmittance targets
        Renderer
            .SetTargets(VraysRadiance[cascade], VraysTransmittance[cascade])
            .Clear(Color.Transparent)
            .Reset()
            .SetShader("HRC/HRC_Extensions")
            .Configure(SamplerState.LinearClamp)
            .SetParameter("PrevRadiance", VraysRadiance[cascade - 1])
            .SetParameter("PrevTransmittance", VraysTransmittance[cascade - 1])
            .SetParameter("PrevSize", CascadeSizes[cascade - 1])
            .SetParameter("CascadeSize", CascadeSizes[cascade])
            .SetParameter("CascadeIndex", new Vector2(cascade, CascadeCount))
            .SetParameter("ProbeScale", ProbeSpacing)
            .Draw()
            .Commit();
    }

    /// <summary>Merges cascade lighting into the reconstructed irradiance field.</summary>
    private void RenderMerging(int cascade)
    {
        int nextCascade = cascade + 1;
        bool hasNext = cascade + 1 < CascadeCount;

        // Alternate outputs while descending. Transmittance is only needed
        // from the ray intervals, never from the already merged far cones.
        Renderer
            .SetTarget(MergeRadiance[cascade % 2])
            .Clear(Color.Transparent)
            .Reset()
            .SetShader("HRC/HRC_MergingCones")
            .Configure(SamplerState.LinearClamp)
            .SetParameter("VraysRadiance", VraysRadiance[cascade])
            .SetParameter("VraysTransmittance", VraysTransmittance[cascade])
            .SetParameter("PrevRadiance", hasNext ? MergeRadiance[nextCascade % 2] : BlackTexture)
            .SetParameter("VraysSize", CascadeSizes[cascade])
            .SetParameter("PrevSize", hasNext ? MergeSizes[nextCascade] : Vector2.One)
            .SetParameter("CascadeSize", MergeSizes[cascade])
            .SetParameter("CascadeIndex", new Vector2(cascade, CascadeCount))
            .SetParameter("ProbeScale", ProbeSpacing)
            .Draw()
            .Commit();
    }

    /// <summary>Copies a cascade result into its persistent frustum output.</summary>
    private void CopyToFrustumOutput(int frustum)
    {
        Renderer.Blit(MergeRadiance[0], FrustumRadiance[frustum]);
    }

    /// <summary>Combines reconstructed lighting and scene materials into the output image.</summary>
    private void Compose()
    {
        LastOutputJitter = OutputJitter;
        Renderer
            .Reset()
            .SetShader("HRC/HRC_FluenceSum")
            .Configure(SamplerState.LinearClamp)
            .SetTarget(FinalTexture)
            .SetParameter("FrustumIndex0", FrustumRadiance[0])
            .SetParameter("FrustumIndex1", FrustumRadiance[1])
            .SetParameter("FrustumIndex2", FrustumRadiance[2])
            .SetParameter("FrustumIndex3", FrustumRadiance[3])
            .SetParameter("WorldSize", WorldSize)
            .SetParameter("ViewSize", ViewSize)
            .SetParameter("OutputJitter", LastOutputJitter)
            .Draw()
            .Commit();
    }

    /// <inheritdoc/>
    public override void Render()
    {
        Renderer.Blit(FinalTexture, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    /// <summary>Linear RGB mean radiance (angular integral divided by 2*pi), at viewport resolution.</summary>
    public RenderTarget2D GetOutput() => FinalTexture;

    /// <inheritdoc/>
    public override void OnResize() => ResizeTargets();

    /// <summary>Releases render targets owned by this stage.</summary>
    private void DisposeRenderTargets()
    {
        FinalTexture?.Dispose();
        FinalTexture = null;

        if (VraysRadiance != null)
        {
            for (int i = 0; i < VraysRadiance.Length; i++)
            {
                VraysRadiance[i]?.Dispose();
                VraysTransmittance[i]?.Dispose();
            }

            VraysRadiance = null;
            VraysTransmittance = null;
        }

        if (MergeRadiance != null)
        {
            foreach (var target in MergeRadiance) target?.Dispose();

            MergeRadiance = null;
        }

        if (FrustumRadiance != null)
        {
            for (int i = 0; i < FrustumCount; i++)
            {
                FrustumRadiance[i]?.Dispose();
            }

            FrustumRadiance = null;
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        Inspector.DestroyWindow("hrcgi");
        Renderer.RenderScaleChanged -= OnRenderScaleChanged;
        DisposeRenderTargets();
    }
}
