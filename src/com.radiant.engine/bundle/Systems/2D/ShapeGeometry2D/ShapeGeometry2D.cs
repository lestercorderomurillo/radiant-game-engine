using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

/// <summary>Opt-in opaque shape geometry for modular renderers. Existing HRC Geometry remains independent.</summary>
[SystemTag("2D", "Geometry")]
public class ShapeGeometry2D : core.System, IRenderSource, ITraceGeometry, ICameraView, IRasterGeometry
{
    /// <summary>Cached shape geometry and material data prepared for rasterization or tracing.</summary>
    private readonly record struct Primitive(int Entity, Transform2D Transform, Vector2 Size, int Kind, Color Color, Vector3 Emission);

    public override RenderLayer RenderLayer => RenderLayer.World;
    public int CircleSegments = 48;
    public bool Rasterize = true;
    public Point? Resolution;
    public RasterSampling Sampling;
    public bool CaptureDepth;
    public RenderTarget2D DepthTexture { get; private set; }

    private int AllocatedSamples;

    public Point ViewSize => Resolution ?? new Point(Renderer.ScreenWidth, Renderer.ScreenHeight);
    public Point Size => Sampling?.RenderSize(ViewSize) ?? ViewSize;
    public int RequestedSamples => Sampling?.RequestedSamples ?? 0;
    public DepthFormat VisibilityFormat => DepthFormat.None;
    public int CameraEntity { get; set; } = -1;
    public Type CameraType => typeof(Camera2D);
    public bool HasCamera { get; private set; } = true;
    public TraceCamera Camera { get; private set; }
    public long Revision { get; private set; }
    public RenderTarget2D ColorTexture { get; private set; }
    public IReadOnlyList<TraceTriangle> Triangles => TriangleData;

    private readonly List<TraceTriangle> TriangleData = new();
    private List<Primitive> Current = new(), Scratch = new();
    private List<Primitive>[] Workers;
    private ColorVertex[] Vertices = Array.Empty<ColorVertex>();
    private readonly Dictionary<string, Func<Texture2D>> Outputs;

    public IReadOnlyDictionary<string, Func<Texture2D>> Buffers => Outputs;

    /// <summary>Registers the 2D geometry buffers exposed to inspection.</summary>
    public ShapeGeometry2D() { Outputs = new() { ["Color"] = () => ColorTexture, ["Depth"] = () => DepthTexture }; }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public Texture2D GetOutput() => ColorTexture;

    /// <inheritdoc/>
    public override void Initialize()
    {
        Workers = new List<Primitive>[ECS.ThreadCount];

        for (int i = 0; i < Workers.Length; i++) Workers[i] = new();
    }

    /// <inheritdoc/>
    public override void Render()
    {
        if (Size.X <= 0 || Size.Y <= 0) throw new global::System.ArgumentOutOfRangeException(nameof(Resolution));
        if (CircleSegments < 8 || CircleSegments > 256) throw new ArgumentOutOfRangeException(nameof(CircleSegments));

        foreach (var worker in Workers) worker.Clear();

        Scene.ECS.Query((int thread, int entity, ref Transform2D t, ref Rectangle2D shape, ref Material m) => Workers[thread].Add(new(entity, t, shape.Size, 0, m.Albedo, m.Emissive.ToVector3() * (m.Emissive.A / 255f))));
        Scene.ECS.Query((int thread, int entity, ref Transform2D t, ref Circle2D shape, ref Material m) => Workers[thread].Add(new(entity, t, new Vector2(shape.Radius), 1, m.Albedo, m.Emissive.ToVector3() * (m.Emissive.A / 255f))));
        Scene.ECS.Query((int thread, int entity, ref Transform2D t, ref Triangle2D shape, ref Material m) => Workers[thread].Add(new(entity, t, shape.Size, shape.Bordered ? 3 : 2, m.Albedo, m.Emissive.ToVector3() * (m.Emissive.A / 255f))));
        Scratch.Clear();

        foreach (var worker in Workers) Scratch.AddRange(worker);

        Scratch.Sort((a, b) => { int order = a.Transform.Layer.CompareTo(b.Transform.Layer); return order != 0 ? order : a.Entity.CompareTo(b.Entity); });

        bool changed = Current.Count != Scratch.Count;
        if (!changed) for (int i = 0; i < Scratch.Count; i++) if (Current[i] != Scratch[i]) { changed = true; break; }

        if (changed || PreviousSegments != CircleSegments)
        {
            (Current, Scratch) = (Scratch, Current);
            BuildTriangles();
            Revision++;
            PreviousSegments = CircleSegments;
        }

        var start = Renderer.ScreenToWorld(Vector2.Zero);
        var end = Renderer.ScreenToWorld(Renderer.ScreenSize);

        if (Resolution.HasValue)
        {
            var center = (start + end) * 0.5f;
            var extent = new Vector2((end.Y - start.Y) * Size.X / Size.Y, end.Y - start.Y);
            start = center - extent * 0.5f;
            end = center + extent * 0.5f;
        }

        Camera = new(Matrix.CreateTranslation(0, 0, -100), Matrix.CreateOrthographicOffCenter(start.X, end.X, end.Y, start.Y, 0.1f, 200), new Vector3(0, 0, 100), true, 200);
        HasCamera = CameraEntity < 0;

        if (CameraEntity >= 0 && Scene.ECS.IsAlive(CameraEntity) && Scene.ECS.HasComponent<Camera2D>(CameraEntity) && !Scene.ECS.IsDisabled(CameraEntity))
        {
            var camera = Scene.ECS.GetComponent<Camera2D>(CameraEntity);
            if (Scene.ECS.HasComponent<Transform2D>(CameraEntity)) camera = camera.WithTransform(Scene.ECS.GetComponent<Transform2D>(CameraEntity));

            var view = Matrix.CreateTranslation(-camera.Position.X, -camera.Position.Y, -100) * Matrix.CreateRotationZ(-camera.Rotation) * Matrix.CreateScale(camera.Zoom, camera.Zoom, 1);
            Camera = Camera with { View = view, Position = Vector3.Transform(Vector3.Zero, MatrixMath.Invert(view)) };
            HasCamera = true;
        }

        if (Sampling != null) Camera = Camera with { Projection = Sampling.Projection(Camera.Projection, ViewSize) };

        if (!Rasterize)
        {
            if (Sampling != null) Sampling.ActualSamples = 0;
            Renderer.SetTarget(null);
            ColorTexture?.Dispose();
            DepthTexture?.Dispose();
            ColorTexture = DepthTexture = null;

            return;
        }

        if (ColorTexture?.Width != Size.X || ColorTexture.Height != Size.Y || AllocatedSamples != (Sampling?.RequestedSamples ?? 0) || (DepthTexture != null) != CaptureDepth)
        {
            Renderer.SetTarget(null);
            ColorTexture?.Dispose();
            DepthTexture?.Dispose();

            int requested = Sampling?.RequestedSamples ?? 0;
            int samples = Renderer.GetSupportedSamples(requested);
            ColorTexture = Renderer.CreateRenderTarget(Size.X, Size.Y, samples: samples);
            DepthTexture = CaptureDepth ? Renderer.CreateRenderTarget(Size.X, Size.Y, SurfaceFormat.Single) : null;
            AllocatedSamples = requested;
            if (Sampling != null) Sampling.ActualSamples = ColorTexture.MultiSampleCount;
        }

        Renderer.Reset().SetTarget(ColorTexture).Clear(Color.Transparent);
        if (HasCamera && Vertices.Length > 0) Renderer.SetShader("Geometry2D").SetTechnique("Default").SetParameter("ViewProjection", Camera.View * Camera.Projection).DrawTriangles(Vertices);

        if (DepthTexture != null)
        {
            Renderer.Commit().Reset().SetTarget(DepthTexture).Clear(Color.Transparent);
            if (HasCamera && Vertices.Length > 0) Renderer.SetShader("Geometry2D").SetTechnique("DepthOnly").SetParameter("View", Camera.View).SetParameter("ViewProjection", Camera.View * Camera.Projection).DrawTriangles(Vertices);
        }

        Renderer.Commit().SetTarget(null);
    }

    private int PreviousSegments;

    /// <summary>Submits geometry for the caller's current render pass.</summary>
    public void Draw(Renderer renderer)
    {
        if (HasCamera && Vertices.Length > 0) renderer.SetTechnique("Shapes").SetParameter("ViewProjection", Camera.View * Camera.Projection).DrawTriangles(Vertices);
    }

    /// <summary>Converts collected 2D primitives into triangles for tracing.</summary>
    private void BuildTriangles()
    {
        TriangleData.Clear();

        foreach (var p in Current)
        {
            if (p.Transform.Scale.X == 0 || p.Transform.Scale.Y == 0) continue;

            if (p.Kind == 0)
            {
                Add(p, Vector2.Zero, new(p.Size.X, 0), p.Size);
                Add(p, Vector2.Zero, p.Size, new(0, p.Size.Y));
            }
            else if (p.Kind == 1)
            {
                for (int i = 0; i < CircleSegments; i++)
                {
                    float a = i * (global::System.MathF.PI * 2) / CircleSegments, b = (i + 1) * (global::System.MathF.PI * 2) / CircleSegments;
                    Add(p, Vector2.Zero, new Vector2(MathF.Cos(a), MathF.Sin(a)) * p.Size.X, new Vector2(MathF.Cos(b), MathF.Sin(b)) * p.Size.X);
                }
            }
            else
            {
                Vector2 a = new(p.Size.X * 0.5f, 0), b = p.Size, c = new(0, p.Size.Y);
                if (p.Kind == 2) Add(p, a, b, c);
                else
                {
                    var center = (a + b + c) / 3;
                    Vector2 ia = Vector2.Lerp(a, center, 0.12f), ib = Vector2.Lerp(b, center, 0.12f), ic = Vector2.Lerp(c, center, 0.12f);
                    Add(p, a, b, ib);
                    Add(p, a, ib, ia);
                    Add(p, b, c, ic);
                    Add(p, b, ic, ib);
                    Add(p, c, a, ia);
                    Add(p, c, ia, ic);
                }
            }
        }

        Vertices = new ColorVertex[TriangleData.Count * 3];

        for (int i = 0; i < TriangleData.Count; i++)
        {
            var triangle = TriangleData[i];
            var color = new Color(triangle.Albedo);
            Vertices[i * 3] = new(triangle.A, color);
            Vertices[i * 3 + 1] = new(triangle.B, color);
            Vertices[i * 3 + 2] = new(triangle.C, color);
        }
    }

    /// <summary>Appends a material-bearing triangle to the tracing geometry.</summary>
    private void Add(Primitive p, Vector2 a, Vector2 b, Vector2 c)
    {
        var matrix = Matrix.CreateScale(p.Transform.Scale.X, p.Transform.Scale.Y, 1) * Matrix.CreateRotationZ(p.Transform.Rotation) * Matrix.CreateTranslation(p.Transform.Position.X, p.Transform.Position.Y, p.Transform.Layer * 0.001f);
        TriangleData.Add(new(Vector3.Transform(new Vector3(a, 0), matrix), Vector3.Transform(new Vector3(b, 0), matrix), Vector3.Transform(new Vector3(c, 0), matrix), p.Color.ToVector3(), p.Emission));
    }

    /// <inheritdoc/>
    public override void Dispose() { ColorTexture?.Dispose(); DepthTexture?.Dispose(); ColorTexture = DepthTexture = null; }
}
