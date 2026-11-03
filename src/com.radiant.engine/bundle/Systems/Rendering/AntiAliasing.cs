using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

public enum PostAAMode { None, FXAA, SMAA, TAA }

/// <summary>Per-view post AA plus supersample resolve. Geometry MSAA/SSAA belongs to RasterSampling.</summary>
[SystemTag("Rendering", "AA")]
public class AntiAliasing : ImageSystem
{
    public PostAAMode Mode = PostAAMode.FXAA;
    public float EdgeThreshold = 0.125f;
    public Func<Point> OutputSize;
    public Func<TraceCamera> Camera;
    public Func<Texture2D> Depth;
    /// <summary>Optional previousUV minus currentUV for moving objects, in normalized UV units.</summary>
    public Func<Texture2D> Motion;
    public Func<long> GeometryRevision;
    public float HistoryWeight = 0.9f;
    public bool JitterEnabled = true;
    private RasterSampling Sampling;
    private RenderTarget2D Resolved, Edges, Weights, History, HistoryDepth, DepthWrite;
    private TraceCamera PreviousCamera;
    private long PreviousRevision = -1;
    private PostAAMode PreviousMode;
    private Point PreviousInputSize;
    private SurfaceFormat PreviousInputFormat;
    public int HistoryFrames { get; private set; }
    public AntiAliasing()
    {
        BufferProviders["Resolved"] = () => Resolved;
        BufferProviders["SMAA edges"] = () => Edges;
        BufferProviders["SMAA weights"] = () => Weights;
        BufferProviders["TAA history"] = () => History;
        BufferProviders["TAA depth"] = () => HistoryDepth;
    }
    public void SetSampling(RasterSampling sampling)
    {
        if (Sampling != null) Sampling.TemporalJitter = null;
        Sampling = sampling;
        sampling.TemporalJitter = () => Enabled && Mode == PostAAMode.TAA && JitterEnabled && Depth?.Invoke() != null ? Jitter(HistoryFrames) : Vector2.Zero;
    }
    public static Vector2 Jitter(int frame) => new(Halton((frame % 8) + 1, 2) - 0.5f, Halton((frame % 8) + 1, 3) - 0.5f);
    private static float Halton(int index, int radix) { float value = 0, fraction = 1f / radix; while (index > 0) { value += (index % radix) * fraction; index /= radix; fraction /= radix; } return value; }
    public void ResetHistory() { HistoryFrames = 0; PreviousRevision = -1; }
    public override Texture2D GetOutput()
    {
        var input = Input?.Invoke();
        if (input == null || !Enabled) { ResetHistory(); return input; }
        if (Mode == PostAAMode.None) return NeedsResolve(input) ? Resolved : input;
        return Mode == PostAAMode.TAA ? History : Output;
    }
    private bool NeedsResolve(Texture2D input) { var size = OutputSize?.Invoke(); return input != null && size.HasValue && (size.Value.X != input.Width || size.Value.Y != input.Height); }
    private void Ensure(ref RenderTarget2D target, int width, int height, SurfaceFormat format)
    {
        if (target?.Width == width && target.Height == height && target.Format == format) return;
        Renderer.SetTarget(null); target?.Dispose(); target = Renderer.CreateRenderTarget(width, height, format); ResetHistory();
    }
    public override void Render()
    {
        var input = Input?.Invoke();
        if (input == null) { ResetHistory(); return; }
        var inputSize = new Point(input.Width, input.Height);
        if (inputSize != PreviousInputSize || input.Format != PreviousInputFormat) ResetHistory();
        PreviousInputSize = inputSize; PreviousInputFormat = input.Format;
        if (Mode != PreviousMode) { ResetHistory(); ReleaseIntermediates(); PreviousMode = Mode; }
        if (NeedsResolve(input))
        {
            var size = OutputSize();
            Ensure(ref Resolved, size.X, size.Y, input.Format);
            Renderer.Reset().SetShader("ImagePipeline").SetTechnique("Resolve").Configure(SamplerState.LinearClamp).SetTarget(Resolved).SetParameter("Source", input).SetParameter("TexelSize", new Vector2(1f / input.Width, 1f / input.Height)).SetParameter("ResolveScale", new Vector2((float)input.Width / size.X, (float)input.Height / size.Y)).Draw().Commit().SetTarget(null);
            input = Resolved;
        }
        if (Mode == PostAAMode.None) return;
        if (Mode == PostAAMode.TAA) { RenderTemporal(input); return; }
        EnsureOutput(input);
        if (Mode == PostAAMode.SMAA) { RenderSmaa(input); return; }
        Renderer.Reset().SetShader("ImagePipeline").SetTechnique("FXAA").Configure(SamplerState.LinearClamp).SetTarget(Output).SetParameter("Source", input).SetParameter("TexelSize", new Vector2(1f / input.Width, 1f / input.Height)).SetParameter("EdgeThreshold", EdgeThreshold).Draw().Commit().SetTarget(null);
    }
    private void RenderSmaa(Texture2D input)
    {
        Ensure(ref Edges, input.Width, input.Height, SurfaceFormat.Color);
        Ensure(ref Weights, input.Width, input.Height, SurfaceFormat.Color);
        var area = Renderer.GetTexture("shaders/AA/SMAA/Textures/AreaTexDX10");
        var search = Renderer.GetTexture("shaders/AA/SMAA/Textures/SearchTex");
        var metrics = new Vector4(1f / input.Width, 1f / input.Height, input.Width, input.Height);
        Renderer.Reset().SetShader("AA/SMAAEdges").Configure(SamplerState.PointClamp, 0).SetTarget(Edges).Clear(Color.Transparent).SetParameter("Metrics", metrics).SetParameter("Source", input).Draw().Commit();
        Renderer.SetShader("AA/SMAAWeights").Configure(SamplerState.LinearClamp, 0).SetParameter("Metrics", metrics).SetTarget(Weights).Clear(Color.Transparent).SetParameter("Edges", Edges).SetParameter("Area", area).SetParameter("Search", search).Draw().Commit();
        Renderer.SetShader("AA/SMAABlend").Configure(SamplerState.LinearClamp, 0).SetParameter("Metrics", metrics).SetTarget(Output).SetParameter("Source", input).SetParameter("Weights", Weights).Draw().Commit().SetTarget(null);
    }
    private void RenderTemporal(Texture2D input)
    {
        if (Camera == null) throw new InvalidOperationException("TAA requires a camera provider for reprojection.");
        EnsureOutput(input, SurfaceFormat.HalfVector4);
        Ensure(ref History, input.Width, input.Height, SurfaceFormat.HalfVector4);
        Ensure(ref HistoryDepth, input.Width, input.Height, SurfaceFormat.Single);
        Ensure(ref DepthWrite, input.Width, input.Height, SurfaceFormat.Single);
        var camera = Camera(); var depth = Depth?.Invoke(); var motion = Motion?.Invoke();
        if (depth == null && motion == null && camera != PreviousCamera) HistoryFrames = 0;
        long revision = GeometryRevision?.Invoke() ?? 0;
        if (revision != PreviousRevision) HistoryFrames = 0;
        if (HistoryFrames > 0 && (Vector3.Distance(camera.Position, PreviousCamera.Position) > 2 || camera.FarPlane != PreviousCamera.FarPlane || camera.Orthographic != PreviousCamera.Orthographic)) HistoryFrames = 0;
        Renderer.Reset().SetShader("AA/Temporal").Configure(SamplerState.LinearClamp, 0).SetTargets(Output, DepthWrite).SetParameter("Source", input).SetParameter("History", History).SetParameter("Depth", depth ?? input).SetParameter("HistoryDepth", HistoryDepth).SetParameter("Motion", motion ?? input).SetParameter("HasDepth", depth != null).SetParameter("HasMotion", motion != null).SetParameter("HasHistory", HistoryFrames > 0).SetParameter("HistoryWeight", Math.Clamp(HistoryWeight, 0, 0.99f)).SetParameter("TexelSize", new Vector2(1f / input.Width, 1f / input.Height)).SetParameter("InverseViewProjection", camera.InverseViewProjection).SetParameter("CurrentProjection", camera.Projection).SetParameter("PreviousViewProjection", PreviousCamera.View * PreviousCamera.Projection).SetParameter("PreviousView", PreviousCamera.View).SetParameter("FarPlane", camera.FarPlane).SetParameter("PreviousFar", PreviousCamera.FarPlane).Draw().Commit().SetTarget(null);
        (Output, History) = (History, Output); (DepthWrite, HistoryDepth) = (HistoryDepth, DepthWrite);
        PreviousCamera = camera; PreviousRevision = revision; HistoryFrames++;
    }
    private void ReleaseIntermediates()
    {
        Resolved?.Dispose(); Edges?.Dispose(); Weights?.Dispose(); History?.Dispose(); HistoryDepth?.Dispose(); DepthWrite?.Dispose();
        Resolved = Edges = Weights = History = HistoryDepth = DepthWrite = null;
    }
    public override void Dispose() { base.Dispose(); ReleaseIntermediates(); ResetHistory(); }
}
