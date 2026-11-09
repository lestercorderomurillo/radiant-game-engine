using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Optional surface-guide buffers produced by the path tracer.</summary>
[Flags]
public enum TraceChannels { None = 0, Albedo = 1, Normals = 2, Depth = 4, Emission = 8, Sample = 16, All = Albedo | Normals | Depth | Emission | Sample }

/// <summary>Selects the path tracer's GPU execution backend, not its lighting model.</summary>
/// <remarks>Auto uses Vulkan ray queries when installed and supported, otherwise shader BVH traversal.
/// Software means shader traversal on the GPU, not CPU rendering.</remarks>
public enum PathTracingBackend { Auto, Software, Vulkan }

/// <summary>Accumulates diffuse global illumination from a 2D or 3D triangle adapter.</summary>
/// <remarks>Provide <see cref="ITraceGeometry"/> through <see cref="Geometry"/>. The image converges over successive samples;
/// use denoising before tone mapping when needed. The current material model is diffuse, without glossy or transparent transport.
/// <see cref="PathTracingBackend"/> selects how rays execute, independently of this lighting model.</remarks>
/// <seealso cref="RasterRenderer"/>
/// <seealso cref="HRCGI"/>
[SystemTag("Rendering", "Path tracing")]
public class PathTracer : ImageSystem
{
    public ITraceGeometry Geometry;
    public PathTracingBackend Backend = PathTracingBackend.Auto;
    public PathTracingBackend ActiveBackend { get; private set; } = PathTracingBackend.Software;
    public string BackendStatus { get; private set; } = "Path tracing not initialized";
    /// <summary>Last completed ray-query dispatch time, or -1 when unavailable; excludes guides and post processing.</summary>
    public float TraceGpuMilliseconds => Hardware?.GpuMilliseconds ?? -1;
    public int HardwareBuildCount => Hardware?.BuildCount ?? 0;
    public int MaxBounces = 4;
    public float ResolutionScale = 0.5f;
    /// <summary>Use stable primary hits for lighting reconstructed by PathLighting.</summary>
    public bool SeparateLighting;
    public Func<Point> ViewSize;
    public Vector3 Environment = new(0.15f);
    public TraceChannels Channels;
    public RenderTarget2D AlbedoTexture => Surfaces[0];
    public RenderTarget2D NormalTexture => Surfaces[1];
    public RenderTarget2D DepthTexture => Surfaces[2];
    public RenderTarget2D EmissionTexture => Surfaces[3];

    private readonly RenderTarget2D[] Surfaces = new RenderTarget2D[4];

    public int SampleCount { get; private set; }
    public RenderTarget2D SampleTexture => (Channels & TraceChannels.Sample) == 0 ? null : Hardware?.Sample ?? SoftwareSample;

    private RenderTarget2D SoftwareSample;

    internal IPathTraceSession Hardware;
    internal int NativeDenoiser;
    internal Func<int> NativeDenoiserProvider;

    private int PreviousNativeDenoiser;
    private PathTracingBackend ResolvedBackend = (PathTracingBackend)(-1);

    public long RadianceRevision { get; private set; }
    public int BvhBuildCount { get; private set; }
    public int NodeCount { get; private set; }

    private Texture2D SceneData;
    private RenderTarget2D History;
    private long UploadedRevision = -1;
    private ITraceGeometry UploadedSource;
    private TraceCamera PreviousCamera;
    private Vector3 PreviousEnvironment;
    private int PreviousBounces;
    private bool PreviousSeparateLighting;
    private int TriangleOffset;
    private int Sequence;

    /// <summary>Registers the tracer's radiance and guide buffers for inspection.</summary>
    public PathTracer()
    {
        BufferProviders["Single sample"] = () => SampleTexture;
        BufferProviders["History"] = () => History;
        BufferProviders["BVH data"] = () => SceneData;
        BufferProviders["Albedo"] = () => AlbedoTexture;
        BufferProviders["Normals"] = () => NormalTexture;
        BufferProviders["Depth"] = () => DepthTexture;
        BufferProviders["Emission"] = () => EmissionTexture;
    }

    /// <summary>Invalidates progressive samples so lighting restarts from the current scene.</summary>
    public void ResetAccumulation() { SampleCount = 0; Sequence = 0; RadianceRevision++; }

    /// <inheritdoc/>
    public override void Initialize()
    {
        if (Geometry == null) throw new InvalidOperationException("PathTracer requires an ITraceGeometry source.");
    }

    /// <summary>Rebuilds and uploads tracing geometry when the source revision changes.</summary>
    private void UploadGeometry()
    {
        var bvh = new TraceBvh(Geometry.Triangles);
        var packed = bvh.Pack(out TriangleOffset);
        int width = Math.Min(1024, Math.Max(1, packed.Length));
        int height = Math.Max(1, (packed.Length + width - 1) / width);
        var pixels = new Vector4[width * height];
        Array.Copy(packed, pixels, packed.Length);
        SceneData?.Dispose();
        SceneData = Renderer.CreateTexture(width, height, SurfaceFormat.Vector4);
        SceneData.SetData(pixels);
        NodeCount = bvh.Nodes.Count;
        BvhBuildCount++;
        global::System.Diagnostics.Debug.WriteLine($"Trace BVH: {NodeCount} nodes, {bvh.Triangles.Length} triangles, {width}x{height}");
        UploadedRevision = Geometry.Revision;
        UploadedSource = Geometry;
        ResetAccumulation();
    }

    /// <summary>Selects a supported tracing backend and records why a requested backend is unavailable.</summary>
    private void ResolveBackend(int Width, int Height)
    {
        if (!Enum.IsDefined(Backend)) throw new ArgumentOutOfRangeException(nameof(Backend));
        if (ResolvedBackend == Backend && (Hardware == null || (Hardware.Sample.Width == Width && Hardware.Sample.Height == Height && Hardware.DenoisingSignals == (NativeDenoiser != 0)))) return;

        string reason = null;
        bool useHardware = Backend != PathTracingBackend.Software && Renderer.SupportsPathTracing(out reason);

        if (Backend == PathTracingBackend.Vulkan && !useHardware) throw new NotSupportedException(reason);

        Renderer.SetTarget(null);
        Hardware?.Dispose();
        Hardware = null;
        SoftwareSample?.Dispose();
        SoftwareSample = null;
        ActiveBackend = PathTracingBackend.Software;
        ResetAccumulation();
        if (Backend == PathTracingBackend.Software) BackendStatus = "GPU shader BVH traversal";
        else if (useHardware)
        {
            Hardware = Renderer.CreatePathTracer(Width, Height, NativeDenoiser != 0);
            ActiveBackend = PathTracingBackend.Vulkan;
            BackendStatus = reason;
        }
        else BackendStatus = reason;
        ResolvedBackend = Backend;
    }

    /// <inheritdoc/>
    public override void Render()
    {
        NativeDenoiser = NativeDenoiserProvider?.Invoke() ?? 0;

        if (Geometry == null) throw new InvalidOperationException("PathTracer requires geometry.");
        if (MaxBounces < 1 || MaxBounces > 16) throw new ArgumentOutOfRangeException(nameof(MaxBounces), "Use 1 to 16 bounces.");
        if (!float.IsFinite(ResolutionScale) || ResolutionScale <= 0 || ResolutionScale > 1) throw new ArgumentOutOfRangeException(nameof(ResolutionScale));

        var size = ViewSize?.Invoke() ?? Geometry.Size;
        int width = Math.Max(1, (int)(size.X * ResolutionScale));
        int height = Math.Max(1, (int)(size.Y * ResolutionScale));
        ResolveBackend(width, height);

        if (Output?.Width != width || Output.Height != height)
        {
            Renderer.SetTarget(null);
            Output?.Dispose();
            History?.Dispose();
            Output = Renderer.CreateRenderTarget(width, height, SurfaceFormat.Vector4);
            History = Renderer.CreateRenderTarget(width, height, SurfaceFormat.Vector4);
            // Establish valid sampled-image layouts even when the first shader sample ignores history.
            Renderer.Reset().SetTarget(Output).Clear(Color.Transparent).SetTarget(History).Clear(Color.Transparent).SetTarget(null);
            ResetAccumulation();
        }

        if (!ReferenceEquals(UploadedSource, Geometry) || UploadedRevision != Geometry.Revision || SceneData == null) UploadGeometry();
        Hardware?.Upload(Geometry);

        var camera = Geometry.Camera;

        if (camera != PreviousCamera) { SampleCount = 0; PreviousCamera = camera; }

        if (Environment != PreviousEnvironment || MaxBounces != PreviousBounces || SeparateLighting != PreviousSeparateLighting || NativeDenoiser != PreviousNativeDenoiser)
        {
            ResetAccumulation();
            PreviousEnvironment = Environment;
            PreviousBounces = MaxBounces;
            PreviousSeparateLighting = SeparateLighting;
            PreviousNativeDenoiser = NativeDenoiser;
        }

        bool captureSample = (Channels & TraceChannels.Sample) != 0;

        if (Hardware == null && (!captureSample || SoftwareSample?.Width != width || SoftwareSample.Height != height))
        {
            Renderer.SetTarget(null);
            SoftwareSample?.Dispose();
            SoftwareSample = null;
            if (captureSample) SoftwareSample = Renderer.CreateRenderTarget(width, height, SurfaceFormat.Vector4);
        }

        RenderSurfaces(width, height);
        Hardware?.Trace(Geometry, Environment, Sequence, MaxBounces, SeparateLighting, NativeDenoiser);

        if (!Geometry.HasCamera)
        {
            Renderer.Reset().SetTarget(Output).Clear(Color.Transparent);
            if (SoftwareSample != null) Renderer.SetTarget(SoftwareSample).Clear(Color.Transparent);
            Renderer.SetTarget(null);
            (Output, History) = (History, Output);
            ResetAccumulation();

            return;
        }

        // First sample ignores history. Float32 storage preserves small corrections from
        // both dark and rare bright samples; half precision biases long-running HDR averages.
        if (Hardware != null)
        {
            Renderer.Reset().SetShader("PathTracer").SetTechnique("External").Configure(SamplerState.PointClamp, 1).Configure(SamplerState.PointClamp, 2).SetTarget(Output)
                .SetParameter("ExternalSample", Hardware.Sample).SetParameter("History", History).SetParameter("SampleIndex", SampleCount).Draw().Commit().SetTarget(null);
        }
        else
        {
            Renderer.Reset().SetShader("PathTracer").SetTechnique(captureSample ? "WithSample" : "Default").Configure(SamplerState.PointClamp, 0).Configure(SamplerState.PointClamp, 1);
            if (captureSample) Renderer.SetTargets(Output, SoftwareSample); else Renderer.SetTarget(Output);
            Renderer.SetParameter("SceneData", SceneData).SetParameter("History", History).SetParameter("DataWidth", SceneData.Width).SetParameter("DataSize", new Vector2(SceneData.Width, SceneData.Height)).SetParameter("TriangleOffset", TriangleOffset).SetParameter("NodeCount", NodeCount).SetParameter("SampleIndex", SampleCount).SetParameter("SequenceIndex", Sequence).SetParameter("MaxBounces", MaxBounces).SetParameter("InverseViewProjection", camera.InverseViewProjection).SetParameter("CameraPosition", camera.Position).SetParameter("Orthographic", camera.Orthographic).SetParameter("Environment", Environment).SetParameter("StablePrimary", SeparateLighting).SetParameter("OutputSize", new Vector2(width, height)).Draw().Commit().SetTarget(null);
        }

        (Output, History) = (History, Output);
        SampleCount = Math.Min(SampleCount + 1, 4096);
        Sequence = (Sequence + 1) & int.MaxValue;
    }

    /// <summary>Produces primary-surface guides for the enabled trace channels.</summary>
    private void RenderSurfaces(int width, int height)
    {
        bool dirty = SampleCount == 0 || !Geometry.HasCamera;

        // Finish resource changes before recording any surface draw commands.
        for (int channel = 0; channel < Surfaces.Length; channel++)
        {
            var target = Surfaces[channel];
            bool requested = ((int)Channels & (1 << channel)) != 0;

            if (!requested && target == null) continue;

            if (!requested || target?.Width != width || target.Height != height)
            {
                Renderer.SetTarget(null);
                target?.Dispose();
                Surfaces[channel] = null;
                if (requested) Surfaces[channel] = Renderer.CreateRenderTarget(width, height, channel == 2 ? SurfaceFormat.Single : channel == 3 ? SurfaceFormat.HalfVector4 : SurfaceFormat.Color, usage: RenderTargetUsage.PreserveContents);
                dirty = true;
            }
        }

        if (!dirty) return;

        for (int channel = 0; channel < Surfaces.Length; channel++)
        {
            if (Surfaces[channel] == null) continue;

            // Surface attributes use pixel-center primary rays, never stochastic history.
            Renderer.Reset().SetTarget(Surfaces[channel]).Clear(Color.Transparent);
            DrawSurface(channel);
            Renderer.SetTarget(null);
        }
    }

    /// <summary>Draws the requested primary-surface channel into the active render target.</summary>
    internal void DrawSurface(int channel)
    {
        if (Geometry.HasCamera) Renderer.SetShader("PathSurfaces").SetTechnique("Surface").Configure(SamplerState.PointClamp, 0).SetParameter("SceneData", SceneData).SetParameter("DataWidth", SceneData.Width).SetParameter("DataSize", new Vector2(SceneData.Width, SceneData.Height)).SetParameter("TriangleOffset", TriangleOffset).SetParameter("NodeCount", NodeCount).SetParameter("InverseViewProjection", Geometry.Camera.InverseViewProjection).SetParameter("SurfaceView", Geometry.Camera.View).SetParameter("SurfaceFar", Geometry.Camera.FarPlane).SetParameter("SurfaceChannel", channel).Draw().Commit();
    }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public override Texture2D GetOutput() => Enabled ? History : Input?.Invoke();

    /// <inheritdoc/>
    public override void Dispose()
    {
        base.Dispose();
        History?.Dispose();
        History = null;
        SoftwareSample?.Dispose();
        SoftwareSample = null;
        Hardware?.Dispose();
        Hardware = null;
        ResolvedBackend = (PathTracingBackend)(-1);
        ActiveBackend = PathTracingBackend.Software;
        SceneData?.Dispose();
        SceneData = null;

        for (int i = 0; i < Surfaces.Length; i++) { Surfaces[i]?.Dispose(); Surfaces[i] = null; }

        UploadedRevision = -1;
        UploadedSource = null;
        ResetAccumulation();
    }
}
