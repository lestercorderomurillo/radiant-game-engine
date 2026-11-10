using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Experimental deterministic diffuse global illumination for 3D trace geometry using world-space Radiance Cascades.</summary>
/// <remarks>
/// Probes fill a cube around the scene; each cascade doubles probe spacing, quadruples directions and quadruples ray length,
/// so near occluders are resolved spatially and distant light directionally. Intervals are traced on the shader BVH when lighting needs updating
/// and merged top-down; shading interpolates a per-probe irradiance map, which also feeds the next frame's bounce.
/// Provide <see cref="ITraceGeometry"/> through <see cref="Geometry"/> and send the output to tone mapping.
/// The material model is diffuse with per-triangle emission, as in <see cref="PathTracer"/>.
/// </remarks>
/// <example>
/// The following example shows how to light meshes with radiance cascades:
/// <code>
/// var meshes = ECS.AddSystem&lt;MeshGeometry3D&gt;();
/// var cascades = ECS.AddSystem(new RadianceCascades { Geometry = meshes, After = [meshes] });
/// var color = ECS.AddSystem(new ToneMapping { Input = cascades.GetOutput, After = [cascades] });
/// </code>
/// </example>
/// <seealso cref="HRCGI"/>
/// <seealso cref="PathTracer"/>
[SystemTag("Rendering", "Radiance cascades")]
public class RadianceCascades : ImageSystem
{
    /// <summary>Largest atlas edge used for cascade and irradiance textures.</summary>
    public const int MaxAtlasSize = 4096;
    public const int MaxCascadeLimit = 6;

    public ITraceGeometry Geometry;
    /// <summary>Output size; defaults to the geometry size.</summary>
    public Func<Point> ViewSize;
    /// <summary>Radiance seen by rays that leave the scene.</summary>
    public Vector3 Environment = new(0.15f);
    /// <summary>Cascade 0 probes per axis: a power of two from 8 to 64.</summary>
    public int GridSize = 32;
    /// <summary>Cascade 0 direction rows: 2, 4 or 8. A cascade stores twice as many columns, so 4 rows give 32 directions.</summary>
    public int BaseDirectionRows = 4;
    /// <summary>Cascade 0 ray length in probe spacings; each further cascade is four times longer.</summary>
    public float IntervalScale = 1.6f;
    /// <summary>Upper limit on cascades; the last cascade's rays continue to infinity.</summary>
    public int MaxCascades = MaxCascadeLimit;
    /// <summary>Feed the previous frame's irradiance into ray hits, converging to multiple bounces.</summary>
    public bool MultiBounce = true;
    /// <summary>Maximum lighting updates after a change before reusing the world-space field. Zero updates continuously; higher limits help highly reflective scenes.</summary>
    public int MaxBounceUpdates = 32;
    /// <summary>Trace one connecting segment per interpolated upper probe so thin walls between probes stop leaking light. Eight rays per direction instead of one.</summary>
    public bool LeakFix = true;
    /// <summary>Lookup offset along the normal in probe spacings, limiting self-shadowing from probes inside walls.</summary>
    public float NormalBias = 0.3f;
    /// <summary>Explicit probe volume (center, edge length); when null the volume fits the geometry bounds.</summary>
    public (Vector3 Center, float Size)? Volume;

    public int CascadeCount { get; private set; }
    public float ProbeSpacing { get; private set; }
    public Vector3 VolumeMin { get; private set; }
    public float VolumeSize { get; private set; }
    /// <summary>Interval segment budget per probe-field update, excluding primary and irradiance visibility rays. Reused fields trace no interval segments.</summary>
    public long RaysPerFrame { get; private set; }
    /// <summary>Bounce updates performed since lighting was last invalidated.</summary>
    public int BounceFrames { get; private set; }
    /// <summary>Total probe-field updates, including direct-light updates, over this system's lifetime.</summary>
    public long ProbeUpdateCount { get; private set; }
    /// <summary>Whether this frame reused a previously computed probe field.</summary>
    public bool ReusedProbeField { get; private set; }
    public int BvhBuildCount => SceneBuffer.BuildCount;
    public RenderTarget2D DepthTexture { get; private set; }
    public RenderTarget2D IrradianceTexture => Irradiance[Current];

    private readonly record struct CascadeLayout(int Grid, int Theta, int TilesPerRow, int Width, int Height, float Spacing, float Start, float End);

    private readonly TraceSceneBuffer SceneBuffer = new();
    private readonly RenderTarget2D[] Cascades = new RenderTarget2D[MaxCascadeLimit];
    private readonly RenderTarget2D[] Irradiance = new RenderTarget2D[2];
    private CascadeLayout[] Layouts = [];
    private int Current;
    private int IrradianceTilesPerRow;
    private int LayoutGrid, LayoutRows, LayoutMaxCascades;
    private float LayoutScale;
    private bool LayoutLeakFix;
    private bool FittedVolume;
    private Vector3 PreviousEnvironment;
    private float PreviousNormalBias;
    private bool PreviousMultiBounce;
    private bool FieldValid;

    // Eight normal columns plus two wrap columns for hardware bilinear sampling.
    private static readonly Point IrradianceTile = new(10, 5);

    /// <summary>Registers cascade, irradiance and depth buffers for inspection.</summary>
    public RadianceCascades()
    {
        for (int cascade = 0; cascade < MaxCascadeLimit; cascade++) { int index = cascade; BufferProviders[$"Cascade {index}"] = () => Cascades[index]; }

        BufferProviders["Irradiance"] = () => Irradiance[Current];
        BufferProviders["Depth"] = () => DepthTexture;
        BufferProviders["BVH data"] = () => SceneBuffer.Data;
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        if (Geometry == null) throw new InvalidOperationException("RadianceCascades requires an ITraceGeometry source.");
    }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public override Texture2D GetOutput() => Enabled ? Output : Input?.Invoke();

    /// <summary>Discards bounce feedback so lighting restarts from direct emission.</summary>
    public void ResetBounces()
    {
        BounceFrames = 0;
        FieldValid = false;
        ReusedProbeField = false;
    }

    /// <inheritdoc/>
    public override void Render()
    {
        if (Geometry == null) throw new InvalidOperationException("RadianceCascades requires geometry.");
        if (GridSize < 8 || GridSize > 64 || (GridSize & (GridSize - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(GridSize), "Use 8, 16, 32 or 64 probes per axis.");
        if (BaseDirectionRows < 2 || BaseDirectionRows > 8 || (BaseDirectionRows & (BaseDirectionRows - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(BaseDirectionRows), "Use 2, 4 or 8 direction rows.");
        if ((long)GridSize * GridSize * GridSize * 2 * BaseDirectionRows * BaseDirectionRows > (long)MaxAtlasSize * MaxAtlasSize) throw new ArgumentOutOfRangeException(nameof(BaseDirectionRows), "A 64³ grid supports at most 4 direction rows.");
        if (!float.IsFinite(IntervalScale) || IntervalScale <= 0) throw new ArgumentOutOfRangeException(nameof(IntervalScale));
        if (MaxCascades < 1 || MaxCascades > MaxCascadeLimit) throw new ArgumentOutOfRangeException(nameof(MaxCascades));
        if (MaxBounceUpdates < 0) throw new ArgumentOutOfRangeException(nameof(MaxBounceUpdates));
        if (!float.IsFinite(NormalBias) || NormalBias < 0) throw new ArgumentOutOfRangeException(nameof(NormalBias));
        if (!float.IsFinite(Environment.X) || !float.IsFinite(Environment.Y) || !float.IsFinite(Environment.Z)) throw new ArgumentOutOfRangeException(nameof(Environment));

        var size = ViewSize?.Invoke() ?? Geometry.Size;
        if (size.X <= 0 || size.Y <= 0) throw new ArgumentOutOfRangeException(nameof(ViewSize), "Output dimensions must be positive.");
        bool rebuilt = SceneBuffer.Update(Renderer, Geometry);
        if (rebuilt || PreviousEnvironment != Environment || PreviousNormalBias != NormalBias || PreviousMultiBounce != MultiBounce) ResetBounces();
        PreviousEnvironment = Environment;
        PreviousNormalBias = NormalBias;
        PreviousMultiBounce = MultiBounce;

        if (Output?.Width != size.X || Output.Height != size.Y)
        {
            Renderer.SetTarget(null);
            Output?.Dispose();
            DepthTexture?.Dispose();
            Output = Renderer.CreateRenderTarget(size.X, size.Y, SurfaceFormat.HalfVector4);
            DepthTexture = Renderer.CreateRenderTarget(size.X, size.Y, SurfaceFormat.Single);
        }

        if (!Geometry.HasCamera || SceneBuffer.TriangleCount == 0)
        {
            ResetBounces();
            Renderer.Reset().SetTarget(Output).Clear(Color.Transparent).SetTarget(DepthTexture).Clear(Color.Transparent).SetTarget(null);
            return;
        }

        ResolveVolume(rebuilt);
        ResolveLayouts();

        ReusedProbeField = FieldValid && (!MultiBounce || (MaxBounceUpdates > 0 && BounceFrames >= MaxBounceUpdates));
        if (!ReusedProbeField)
        {
            for (int cascade = CascadeCount - 1; cascade >= 0; cascade--) RenderIntervals(cascade);

            RenderIrradiance();
            FieldValid = true;
            ProbeUpdateCount++;
        }
        RenderGather();
    }

    /// <summary>Chooses the probe cube from the explicit volume or the geometry bounds with hysteresis.</summary>
    private void ResolveVolume(bool rebuilt)
    {
        if (Volume is { } explicitVolume)
        {
            if (!float.IsFinite(explicitVolume.Size) || explicitVolume.Size <= 0 || !float.IsFinite(explicitVolume.Center.X) || !float.IsFinite(explicitVolume.Center.Y) || !float.IsFinite(explicitVolume.Center.Z)) throw new ArgumentOutOfRangeException(nameof(Volume));

            FittedVolume = false;
            ApplyVolume(explicitVolume.Center, explicitVolume.Size);
            return;
        }

        if (FittedVolume && !rebuilt) return;

        var extent = SceneBuffer.BoundsMax - SceneBuffer.BoundsMin;
        // Probes stay inside the bounds: a layer beyond a boundary wall would merge outside radiance into interior probes.
        float size = MathF.Max(MathF.Max(extent.X, extent.Y), MathF.Max(extent.Z, 1e-3f));
        var center = (SceneBuffer.BoundsMin + SceneBuffer.BoundsMax) * 0.5f;
        var currentMax = VolumeMin + new Vector3(VolumeSize);
        bool contained = FittedVolume && SceneBuffer.BoundsMin.X >= VolumeMin.X && SceneBuffer.BoundsMin.Y >= VolumeMin.Y && SceneBuffer.BoundsMin.Z >= VolumeMin.Z
            && SceneBuffer.BoundsMax.X <= currentMax.X && SceneBuffer.BoundsMax.Y <= currentMax.Y && SceneBuffer.BoundsMax.Z <= currentMax.Z;

        // Moving geometry keeps the current volume while it fits and has not shrunk below half of it.
        if (contained && size >= VolumeSize * 0.5f) return;

        FittedVolume = true;
        ApplyVolume(center, size);
    }

    /// <summary>Applies a probe cube, invalidating cascades and bounce feedback when it moves.</summary>
    private void ApplyVolume(Vector3 center, float size)
    {
        var min = center - new Vector3(size * 0.5f);

        if (min == VolumeMin && size == VolumeSize) return;

        VolumeMin = min;
        VolumeSize = size;
        DisposeCascades();
    }

    /// <summary>Recomputes cascade dimensions when quality settings or the volume changed.</summary>
    private void ResolveLayouts()
    {
        if (Cascades[0] != null && LayoutGrid == GridSize && LayoutRows == BaseDirectionRows && LayoutScale == IntervalScale && LayoutLeakFix == LeakFix && LayoutMaxCascades == MaxCascades) return;

        DisposeCascades();

        float spacing = VolumeSize / GridSize;
        float baseLength = IntervalScale * spacing;
        float diagonal = VolumeSize * MathF.Sqrt(3);
        int depth = 1;
        for (int grid = GridSize; grid > 1; grid >>= 1) depth++;
        int count = Math.Clamp((int)MathF.Ceiling(MathF.Log(MathF.Max(diagonal / baseLength, 1), 4)) + 1, 1, Math.Min(MaxCascades, depth));
        Layouts = new CascadeLayout[count];
        RaysPerFrame = 0;

        for (int cascade = 0; cascade < count; cascade++)
        {
            int grid = GridSize >> cascade;
            int theta = BaseDirectionRows << cascade;
            int probes = grid * grid * grid;
            int tilesPerRow = Math.Min(probes, MaxAtlasSize / (2 * theta));
            if (tilesPerRow < 1) throw new InvalidOperationException($"Cascade {cascade} directions exceed the {MaxAtlasSize} texel atlas limit.");
            int height = probes / tilesPerRow * theta;
            if (height > MaxAtlasSize) throw new InvalidOperationException($"Cascade {cascade} needs a {height} texel atlas; reduce GridSize or BaseDirectionRows.");
            float start = cascade == 0 ? 0 : baseLength * MathF.Pow(4, cascade - 1);
            Layouts[cascade] = new(grid, theta, tilesPerRow, tilesPerRow * 2 * theta, height, VolumeSize / grid, start, baseLength * MathF.Pow(4, cascade));
            Cascades[cascade] = Renderer.CreateRenderTarget(Layouts[cascade].Width, Layouts[cascade].Height, SurfaceFormat.HalfVector4);
            RaysPerFrame += (long)Layouts[cascade].Width * Layouts[cascade].Height * (LeakFix && cascade + 1 < count ? 8 : 1);
        }

        int baseProbes = GridSize * GridSize * GridSize;
        IrradianceTilesPerRow = Math.Min(baseProbes, MaxAtlasSize / IrradianceTile.X);

        for (int index = 0; index < Irradiance.Length; index++)
            Irradiance[index] = Renderer.CreateRenderTarget(IrradianceTilesPerRow * IrradianceTile.X, (baseProbes + IrradianceTilesPerRow - 1) / IrradianceTilesPerRow * IrradianceTile.Y, SurfaceFormat.HalfVector4);

        CascadeCount = count;
        ProbeSpacing = spacing;
        LayoutGrid = GridSize;
        LayoutRows = BaseDirectionRows;
        LayoutScale = IntervalScale;
        LayoutLeakFix = LeakFix;
        LayoutMaxCascades = MaxCascades;
        BounceFrames = 0;
    }

    /// <summary>Traces one cascade's radiance intervals and merges the next cascade's cones into them.</summary>
    private void RenderIntervals(int cascade)
    {
        var layout = Layouts[cascade];
        bool hasUpper = cascade + 1 < CascadeCount;
        bool feedback = MultiBounce && BounceFrames > 0;
        var upper = hasUpper ? Layouts[cascade + 1] : layout;

        Renderer.Reset().SetShader("RC/RC_Intervals").Configure(SamplerState.PointClamp, 0).Configure(SamplerState.LinearClamp, 1).Configure(SamplerState.LinearClamp, 2).SetTarget(Cascades[cascade]);
        SceneBuffer.Bind(Renderer)
            .SetParameter("AtlasSize", new Vector2(layout.Width, layout.Height)).SetParameter("TilesPerRow", layout.TilesPerRow).SetParameter("GridSize", layout.Grid).SetParameter("Theta", layout.Theta)
            .SetParameter("Spacing", layout.Spacing).SetParameter("GridMin", VolumeMin).SetParameter("IntervalStart", layout.Start).SetParameter("IntervalEnd", layout.End)
            .SetParameter("LeakFix", LeakFix).SetParameter("Environment", Environment)
            .SetParameter("HasUpper", hasUpper).SetParameter("Upper", hasUpper ? Cascades[cascade + 1] : SceneBuffer.Data)
            .SetParameter("UpperAtlasSize", new Vector2(upper.Width, upper.Height)).SetParameter("UpperTilesPerRow", upper.TilesPerRow).SetParameter("UpperGridSize", upper.Grid).SetParameter("UpperTheta", upper.Theta).SetParameter("UpperSpacing", upper.Spacing)
            .SetParameter("HasFeedback", feedback).SetParameter("Feedback", Irradiance[Current]).SetParameter("FeedbackAtlasSize", new Vector2(Irradiance[Current].Width, Irradiance[Current].Height))
            .SetParameter("FeedbackTilesPerRow", IrradianceTilesPerRow).SetParameter("FeedbackTileSize", new Vector2(IrradianceTile.X, IrradianceTile.Y)).SetParameter("FeedbackGridSize", GridSize)
            .SetParameter("FeedbackSpacing", ProbeSpacing).SetParameter("NormalBias", NormalBias)
            .Draw().Commit().SetTarget(null);
    }

    /// <summary>Integrates merged cascade 0 into the per-probe irradiance map read by shading and the next frame.</summary>
    private void RenderIrradiance()
    {
        var layout = Layouts[0];
        int next = Current ^ 1;

        Renderer.Reset().SetShader("RC/RC_Irradiance").Configure(SamplerState.PointClamp, 0).SetTarget(Irradiance[next])
            .SetParameter("Cascade", Cascades[0]).SetParameter("CascadeAtlasSize", new Vector2(layout.Width, layout.Height)).SetParameter("CascadeTilesPerRow", layout.TilesPerRow).SetParameter("Theta", layout.Theta)
            .SetParameter("TilesPerRow", IrradianceTilesPerRow).SetParameter("TileSize", new Vector2(IrradianceTile.X, IrradianceTile.Y)).SetParameter("GridSize", GridSize)
            .Draw().Commit().SetTarget(null);

        Current = next;
        BounceFrames = MultiBounce ? Math.Min(BounceFrames + 1, int.MaxValue - 1) : 0;
    }

    /// <summary>Shades primary hits at output resolution and writes linear depth.</summary>
    private void RenderGather()
    {
        var camera = Geometry.Camera;
        var volume = Irradiance[Current];

        Renderer.Reset().SetShader("RC/RC_Gather").Configure(SamplerState.PointClamp, 0).Configure(SamplerState.LinearClamp, 1).SetTargets(Output, DepthTexture);
        SceneBuffer.Bind(Renderer)
            .SetParameter("Irradiance", volume).SetParameter("InverseViewProjection", camera.InverseViewProjection).SetParameter("View", camera.View).SetParameter("FarPlane", camera.FarPlane)
            .SetParameter("Environment", Environment).SetParameter("HasCamera", true)
            .SetParameter("AtlasSize", new Vector2(volume.Width, volume.Height)).SetParameter("TilesPerRow", IrradianceTilesPerRow).SetParameter("TileSize", new Vector2(IrradianceTile.X, IrradianceTile.Y))
            .SetParameter("GridSize", GridSize).SetParameter("GridMin", VolumeMin).SetParameter("Spacing", ProbeSpacing).SetParameter("NormalBias", NormalBias)
            .Draw().Commit().SetTarget(null);
    }

    /// <summary>Releases cascade and irradiance atlases; the next frame recreates them and restarts bounces.</summary>
    private void DisposeCascades()
    {
        Renderer.SetTarget(null);

        for (int cascade = 0; cascade < Cascades.Length; cascade++) { Cascades[cascade]?.Dispose(); Cascades[cascade] = null; }

        for (int index = 0; index < Irradiance.Length; index++) { Irradiance[index]?.Dispose(); Irradiance[index] = null; }

        Layouts = [];
        CascadeCount = 0;
        ResetBounces();
        RaysPerFrame = 0;
        ProbeSpacing = 0;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        Renderer.SetTarget(null);
        base.Dispose();
        DepthTexture?.Dispose();
        DepthTexture = null;
        DisposeCascades();
        SceneBuffer.Dispose();
        FittedVolume = false;
        VolumeSize = 0;
    }
}
