using System.Collections.Generic;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Mesh, transform, and material data extracted from one ECS entity.</summary>
public readonly record struct MeshInstance(int Entity, Mesh Mesh, Matrix World, Color Color, Vector3 Emission, bool DoubleSided = false);

/// <summary>CPU scene extraction only. Shared by flat/raster geometry and the path tracer; no GPU targets.</summary>
[SystemTag("3D", "Geometry")]
public class MeshGeometry3D : core.System, ITraceGeometry, ICameraView
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    public int CameraEntity { get; set; } = -1;
    public global::System.Type CameraType => typeof(Camera3D);
    public Point? Resolution;
    public RasterSampling Sampling;
    public Point ViewSize => Resolution ?? new Point(Renderer.ScreenWidth, Renderer.ScreenHeight);
    public Point Size => Sampling?.RenderSize(ViewSize) ?? ViewSize;
    public bool HasCamera { get; private set; }
    public TraceCamera Camera { get; private set; }
    public long Revision { get; private set; }
    public IReadOnlyList<MeshInstance> Instances => Current;

    private List<MeshInstance> Current = new(), Scratch = new();
    private List<MeshInstance>[] Workers;
    private List<(int Entity, Camera3D Camera)>[] Cameras;
    private readonly List<TraceTriangle> CachedTriangles = new();
    private long TriangleRevision = -1;

    public IReadOnlyList<TraceTriangle> Triangles
    {
        get
        {
            if (TriangleRevision == Revision) return CachedTriangles;

            CachedTriangles.Clear();

            foreach (var instance in Current)
                for (int i = 0; i < instance.Mesh.Indices.Length; i += 3)
                {
                    var mesh = instance.Mesh;
                    var a = Vector3.Transform(mesh.Vertices[mesh.Indices[i]].Position, instance.World);
                    var b = Vector3.Transform(mesh.Vertices[mesh.Indices[i + 1]].Position, instance.World);
                    var c = Vector3.Transform(mesh.Vertices[mesh.Indices[i + 2]].Position, instance.World);
                    if (Vector3.Cross(b - a, c - a).LengthSquared() > 1e-12f) CachedTriangles.Add(new(a, b, c, instance.Color.ToVector3(), instance.Emission));
                }

            TriangleRevision = Revision;

            return CachedTriangles;
        }
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        Workers = new List<MeshInstance>[ECS.ThreadCount];
        Cameras = new List<(int, Camera3D)>[ECS.ThreadCount];

        for (int i = 0; i < Workers.Length; i++) { Workers[i] = new(); Cameras[i] = new(); }
    }

    /// <inheritdoc/>
    public override void Render()
    {
        if (Size.X <= 0 || Size.Y <= 0) throw new global::System.ArgumentOutOfRangeException(nameof(Resolution));

        foreach (var worker in Workers) worker.Clear();

        Scene.ECS.Query((int thread, int entity, ref Transform3D transform, ref Mesh3D mesh) =>
        {
            if (mesh.Geometry != null && global::System.MathF.Abs(transform.Scale.X * transform.Scale.Y * transform.Scale.Z) > 1e-8f) Workers[thread].Add(new(entity, mesh.Geometry, transform.Matrix, mesh.Color, Vector3.Zero, mesh.DoubleSided));
        });
        Scratch.Clear();

        foreach (var worker in Workers)
            foreach (var instance in worker)
            {
                var emission = Scene.ECS.HasComponent<SurfaceEmission>(instance.Entity) ? Scene.ECS.GetComponent<SurfaceEmission>(instance.Entity).Radiance : Vector3.Zero;
                Scratch.Add(instance with { Emission = emission });
            }

        bool changed = Current.Count != Scratch.Count;
        if (!changed) for (int i = 0; i < Scratch.Count; i++) if (Current[i] != Scratch[i]) { changed = true; break; }

        if (changed) { (Current, Scratch) = (Scratch, Current); Revision++; }

        foreach (var camera in Cameras) camera.Clear();

        Scene.ECS.Query((int thread, int entity, ref Camera3D camera) => Cameras[thread].Add((entity, camera)));
        HasCamera = false;

        int selected = int.MaxValue;

        foreach (var worker in Cameras)
            foreach (var entry in worker)
                if ((CameraEntity < 0 || CameraEntity == entry.Entity) && entry.Entity < selected)
                {
                    selected = entry.Entity;

                    var camera = entry.Camera;
                    if (Scene.ECS.HasComponent<Transform3D>(entry.Entity)) camera = camera.WithTransform(Scene.ECS.GetComponent<Transform3D>(entry.Entity));
                    Camera = new(camera.View, camera.Projection((float)Size.X / Size.Y), camera.Position, false, camera.FarPlane);
                    if (Sampling != null) Camera = Camera with { Projection = Sampling.Projection(Camera.Projection, ViewSize) };
                    HasCamera = true;
                }
    }
}
