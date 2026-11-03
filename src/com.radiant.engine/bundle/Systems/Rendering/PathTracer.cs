using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

[Flags]
public enum TraceChannels { None = 0, Albedo = 1, Normals = 2, Depth = 4, Emission = 8, Sample = 16, All = Albedo | Normals | Depth | Emission | Sample }

/// <summary>Progressive diffuse GPU path tracing over an opt-in 2D or 3D triangle adapter.</summary>
[SystemTag("Rendering", "Path tracing")]
public class PathTracer : ImageSystem
{
    public ITraceGeometry Geometry;
    public int MaxBounces = 4;
    public float ResolutionScale = 0.5f;
    public Func<Point> ViewSize;
    public Vector3 Environment = new(0.15f);
    public TraceChannels Channels;
    public RenderTarget2D AlbedoTexture => Surfaces[0];
    public RenderTarget2D NormalTexture => Surfaces[1];
    public RenderTarget2D DepthTexture => Surfaces[2];
    public RenderTarget2D EmissionTexture => Surfaces[3];
    private readonly RenderTarget2D[] Surfaces = new RenderTarget2D[4];
    public int SampleCount { get; private set; }
    public RenderTarget2D SampleTexture { get; private set; }
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
    private int TriangleOffset;
    private int Sequence;
    public PathTracer()
    {
        BufferProviders["Single sample"] = () => SampleTexture;
        BufferProviders["History"] = () => History; BufferProviders["BVH data"] = () => SceneData;
        BufferProviders["Albedo"] = () => AlbedoTexture; BufferProviders["Normals"] = () => NormalTexture;
        BufferProviders["Depth"] = () => DepthTexture; BufferProviders["Emission"] = () => EmissionTexture;
    }
    public void ResetAccumulation() { SampleCount = 0; Sequence = 0; RadianceRevision++; }
    public override void Initialize()
    {
        if (Geometry == null) throw new InvalidOperationException("PathTracer requires an ITraceGeometry source.");
    }
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
    public override void Render()
    {
        if (Geometry == null) throw new InvalidOperationException("PathTracer requires geometry.");
        if (MaxBounces < 1 || MaxBounces > 16) throw new ArgumentOutOfRangeException(nameof(MaxBounces), "Use 1 to 16 bounces.");
        if (!float.IsFinite(ResolutionScale) || ResolutionScale <= 0 || ResolutionScale > 1) throw new ArgumentOutOfRangeException(nameof(ResolutionScale));
        var size = ViewSize?.Invoke() ?? Geometry.Size;
        int width = Math.Max(1, (int)(size.X * ResolutionScale));
        int height = Math.Max(1, (int)(size.Y * ResolutionScale));
        if (Output?.Width != width || Output.Height != height)
        {
            Renderer.SetTarget(null);
            Output?.Dispose(); History?.Dispose();
            Output = Renderer.CreateRenderTarget(width, height, SurfaceFormat.Vector4);
            History = Renderer.CreateRenderTarget(width, height, SurfaceFormat.Vector4);
            ResetAccumulation();
        }
        if (!ReferenceEquals(UploadedSource, Geometry) || UploadedRevision != Geometry.Revision || SceneData == null) UploadGeometry();
        var camera = Geometry.Camera;
        if (camera != PreviousCamera) { SampleCount = 0; PreviousCamera = camera; }
        if (Environment != PreviousEnvironment || MaxBounces != PreviousBounces)
        {
            ResetAccumulation(); PreviousEnvironment = Environment; PreviousBounces = MaxBounces;
        }
        bool captureSample = (Channels & TraceChannels.Sample) != 0;
        if (!captureSample || SampleTexture?.Width != width || SampleTexture.Height != height)
        {
            Renderer.SetTarget(null); SampleTexture?.Dispose(); SampleTexture = null;
            if (captureSample) SampleTexture = Renderer.CreateRenderTarget(width, height, SurfaceFormat.Vector4);
        }
        RenderSurfaces(width, height, camera);
        if (!Geometry.HasCamera)
        {
            Renderer.Reset().SetTarget(Output).Clear(Color.Transparent);
            if (SampleTexture != null) Renderer.SetTarget(SampleTexture).Clear(Color.Transparent);
            Renderer.SetTarget(null);
            (Output, History) = (History, Output);
            ResetAccumulation();
            return;
        }
        // First sample ignores undefined history. Float32 storage preserves small corrections from
        // both dark and rare bright samples; half precision biases long-running HDR averages.
        Renderer.Reset().SetShader("PathTracer").SetTechnique(captureSample ? "WithSample" : "Default").Configure(SamplerState.PointClamp, 0).Configure(SamplerState.PointClamp, 1);
        if (captureSample) Renderer.SetTargets(Output, SampleTexture); else Renderer.SetTarget(Output);
        Renderer.SetParameter("SceneData", SceneData).SetParameter("History", History).SetParameter("DataWidth", SceneData.Width).SetParameter("DataSize", new Vector2(SceneData.Width, SceneData.Height)).SetParameter("TriangleOffset", TriangleOffset).SetParameter("NodeCount", NodeCount).SetParameter("SampleIndex", SampleCount).SetParameter("SequenceIndex", Sequence).SetParameter("MaxBounces", MaxBounces).SetParameter("InverseViewProjection", camera.InverseViewProjection).SetParameter("CameraPosition", camera.Position).SetParameter("Orthographic", camera.Orthographic).SetParameter("Environment", Environment).SetParameter("OutputSize", new Vector2(width, height)).Draw().Commit().SetTarget(null);
        (Output, History) = (History, Output);
        SampleCount = Math.Min(SampleCount + 1, 4096);
        Sequence = (Sequence + 1) & int.MaxValue;
    }
    private void RenderSurfaces(int width, int height, TraceCamera camera)
    {
        for (int i = 0; i < Surfaces.Length; i++)
        {
            var target = Surfaces[i];
            bool requested = ((int)Channels & (1 << i)) != 0;
            if (!requested && target == null) continue;
            bool dirty = SampleCount == 0 || !Geometry.HasCamera;
            if (!requested || target?.Width != width || target.Height != height)
            {
                Renderer.SetTarget(null); target?.Dispose(); Surfaces[i] = null;
                if (!requested) continue;
                Surfaces[i] = target = Renderer.CreateRenderTarget(width, height, i == 2 ? SurfaceFormat.Single : i == 3 ? SurfaceFormat.HalfVector4 : SurfaceFormat.Color);
                dirty = true;
            }
            if (!dirty) continue;
            // Surface attributes use pixel-center primary rays, never the stochastic history.
            Renderer.Reset().SetTarget(target).Clear(Color.Transparent);
            if (Geometry.HasCamera) Renderer.SetShader("PathTracer").SetTechnique("Surface").Configure(SamplerState.PointClamp, 0).SetParameter("SceneData", SceneData).SetParameter("DataWidth", SceneData.Width).SetParameter("DataSize", new Vector2(SceneData.Width, SceneData.Height)).SetParameter("TriangleOffset", TriangleOffset).SetParameter("NodeCount", NodeCount).SetParameter("InverseViewProjection", camera.InverseViewProjection).SetParameter("SurfaceView", camera.View).SetParameter("SurfaceFar", camera.FarPlane).SetParameter("SurfaceChannel", i).Draw().Commit();
            Renderer.SetTarget(null);
        }
    }
    public override Texture2D GetOutput() => Enabled ? History : Input?.Invoke();
    public override void Dispose()
    {
        base.Dispose();
        History?.Dispose(); History = null;
        SampleTexture?.Dispose(); SampleTexture = null;
        SceneData?.Dispose(); SceneData = null;
        for (int i = 0; i < Surfaces.Length; i++) { Surfaces[i]?.Dispose(); Surfaces[i] = null; }
        UploadedRevision = -1; UploadedSource = null;
        ResetAccumulation();
    }
}
