using System;
using System.IO;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

/// <summary>Runs raster visibility checks and records diagnostic frames.</summary>
internal sealed class RasterVisibilityWindow : Window
{
    private readonly CornellBoxScene Scene = new() { InitialRenderMethod = 1 };
    private int Frame;
    private HalfVector4[] Previous;
    private readonly (Point Size, RasterAAMode AA, bool Mirrored, bool BackView, bool DoubleSided)[] Cases =
    [
        (new(1920, 1080), RasterAAMode.None, false, false, false),
        (new(1920, 1080), RasterAAMode.MSAA4x, false, false, false),
        (new(1920, 1080), RasterAAMode.SSAA2x, false, false, false),
        (new(3840, 2160), RasterAAMode.None, true, false, false),
        (new(3840, 2160), RasterAAMode.MSAA4x, true, false, false),
        (new(1920, 1080), RasterAAMode.None, false, true, false),
        (new(1920, 1080), RasterAAMode.None, false, true, true)
    ];

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public RasterVisibilityWindow() : base(new GameLoop(), new() { Title = "Raster visibility regression", Width = 960, Height = 540 }) { GameLoop.AddScene(Scene); GameLoop.ThrottleUnfocused = false; }

    /// <inheritdoc/>
    protected override void Update(GameTime time)
    {
        base.Update(time);

        if (Frame % 6 != 0 || Frame / 6 >= Cases.Length) return;

        var test = Cases[Frame / 6];
        Scene.Meshes.Resolution = test.Size;
        Scene.Sampling.Mode = test.AA;
        Scene.ECS.Query((int thread, int entity, ref Transform3D transform, ref Mesh3D mesh) =>
        {
            transform.Scale.X = Math.Abs(transform.Scale.X) * (test.Mirrored ? -1 : 1);
            // Isolate one open plane when validating explicit two-sided rasterization.
            if (test.DoubleSided && (mesh.Geometry != Mesh.Plane || transform.Position.Z > -2)) mesh.Geometry = null;
            mesh.DoubleSided = test.DoubleSided;
        });
        if (test.BackView) Scene.ECS.GetComponent<Camera3D>(Scene.Meshes.CameraEntity).Position = new Vector3(0, 3.4f, -11);
        Previous = null;
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        Frame++;

        if (Frame % 6 != 5 && Frame % 6 != 0) return;

        var target = (RenderTarget2D)Scene.Raster.GetOutput();
        var pixels = new HalfVector4[target.Width * target.Height];
        target.GetData(pixels);

        if (Frame % 6 == 5) { Previous = pixels; return; }

        for (int i = 0; i < pixels.Length; i++) if (pixels[i].PackedValue != Previous[i].PackedValue) throw new Exception("Stationary raster visibility flickers.");

        var triangles = Scene.Meshes.Triangles;
        var normals = new Vector3[triangles.Count];
        var doubleSided = new bool[triangles.Count];
        int triangleIndex = 0;

        foreach (var instance in Scene.Meshes.Instances)
            for (int i = 0; i < instance.Mesh.TriangleCount; i++)
            {
                var t = triangles[triangleIndex];
                doubleSided[triangleIndex] = instance.DoubleSided;
                normals[triangleIndex++] = Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A)) * Math.Sign(instance.World.GetDeterminant());
            }

        var camera = Scene.Meshes.Camera;
        var inverse = camera.InverseViewProjection;
        Vector4 Reference(float x, float y)
        {
            var clip = new Vector4(x * 2 / target.Width - 1, 1 - y * 2 / target.Height, 0, 1);
            var near = Vector4.Transform(clip, inverse);
            near /= near.W;
            clip.Z = 1;

            var far = Vector4.Transform(clip, inverse);
            far /= far.W;

            var origin = new Vector3(near.X, near.Y, near.Z);
            var direction = Vector3.Normalize(new Vector3(far.X, far.Y, far.Z) - origin);
            float closest = float.MaxValue;
            Vector4 result = Vector4.Zero;

            for (int index = 0; index < triangles.Count; index++)
            {
                if (!doubleSided[index] && Vector3.Dot(normals[index], direction) >= 0) continue;

                var triangle = triangles[index];
                var e1 = triangle.B - triangle.A;
                var e2 = triangle.C - triangle.A;
                var p = Vector3.Cross(direction, e2);
                float determinant = Vector3.Dot(e1, p);

                if (MathF.Abs(determinant) < 1e-8f) continue;

                var t = origin - triangle.A;
                float u = Vector3.Dot(t, p) / determinant;
                var q = Vector3.Cross(t, e1);
                float v = Vector3.Dot(direction, q) / determinant;
                float d = Vector3.Dot(e2, q) / determinant;

                if (u < 0 || v < 0 || u + v > 1 || d <= 0 || d >= closest) continue;

                closest = d;

                var normal = normals[index];
                var radiance = triangle.Albedo * (new Vector3(Scene.Raster.Ambient) + MathF.Max(Vector3.Dot(normal, Scene.Raster.LightDirection), 0) * Scene.Raster.LightColor);
                result = new Vector4(radiance, 1);
            }

            return result;
        }

        int errors = 0, compared = 0;

        for (int y = 0; y < target.Height; y++) for (int x = 0; x < target.Width; x++)
        {
            var actual = pixels[y * target.Width + x].ToVector4().ToNumerics();
            var expected = Reference(x + 0.5f, y + 0.5f);
            compared++;

            if (Vector4.Distance(actual, expected) < 0.01f) continue;

            // Allow any coverage mixture of surfaces genuinely intersecting this pixel.
            var minimum = expected;
            var maximum = expected;

            for (int dy = 0; dy <= 8; dy++) for (int dx = 0; dx <= 8; dx++)
            {
                var corner = Reference(x + dx / 8f, y + dy / 8f);
                minimum = Vector4.Min(minimum, corner);
                maximum = Vector4.Max(maximum, corner);
            }

            if (actual.X >= minimum.X - 0.01f && actual.Y >= minimum.Y - 0.01f && actual.Z >= minimum.Z - 0.01f && actual.W >= minimum.W - 0.01f && actual.X <= maximum.X + 0.01f && actual.Y <= maximum.Y + 0.01f && actual.Z <= maximum.Z + 0.01f && actual.W <= maximum.W + 0.01f) continue;

            if (errors++ < 12) Console.WriteLine($"Invalid visibility {x},{y}: {actual}, expected range {minimum} .. {maximum}");
        }

        Directory.CreateDirectory("artifacts/raster-visibility");

        var test = Cases[Frame / 6 - 1];
        string name = $"{test.Size.X}x{test.Size.Y}-{test.AA}-{(test.Mirrored ? "mirrored" : "regular")}{(test.BackView ? "-back" : "")}{(test.DoubleSided ? "-double-sided" : "")}";

        using (var stream = File.Create($"artifacts/raster-visibility/{name}.png")) target.SaveAsPng(stream, target.Width, target.Height);

        Console.WriteLine($"Visibility {name}: {errors}/{compared} invalid pixels");

        if (errors > 0) throw new Exception("Raster renders surfaces outside the pixel's visible coverage.");

        if (Frame == Cases.Length * 6) Exit();
    }
}
