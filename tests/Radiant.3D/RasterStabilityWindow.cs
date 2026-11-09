using System;
using System.Linq;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;

/// <summary>Runs raster stability checks and records diagnostic frames.</summary>
internal sealed class RasterStabilityWindow : Window
{
    private readonly CornellBoxScene Scene = new();
    private int Frame;
    private Color[] Previous;
    private int Errors;
    private TraceCamera LastCamera;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public RasterStabilityWindow() : base(new GameLoop(), new() { Title = "Raster stability regression", Width = 640, Height = 360 }) { GameLoop.AddScene(Scene); GameLoop.ThrottleUnfocused = false; }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);

        if (++Frame < 5) return;

        var target = Scene.Geometry.ColorTexture;
        var pixels = new Color[target.Width * target.Height];
        target.GetData(pixels);

        int changed = Previous == null ? 0 : pixels.Where((p, i) => p != Previous[i]).Count();
        if (Previous != null && changed > 0)
            foreach (int index in Enumerable.Range(0, pixels.Length).Where(i => pixels[i] != Previous[i]).Take(24)) Console.WriteLine($"Changed {index % target.Width},{index / target.Width}: {Previous[index]} -> {pixels[index]}");
        Previous = pixels;

        int tested = 0, errors = 0;
        var camera = Scene.Meshes.Camera;

        if (Frame > 5 && LastCamera != camera) throw new Exception("Static raster test camera changed.");

        LastCamera = camera;

        var inverse = camera.InverseViewProjection;

        for (int y = 3; y < target.Height; y++)
            for (int x = 3; x < target.Width; x++)
            {
                var clip = new Vector4((x + 0.5f) * 2 / target.Width - 1, 1 - (y + 0.5f) * 2 / target.Height, 0, 1);
                var near = Vector4.Transform(clip, inverse);
                near /= near.W;
                clip.Z = 1;

                var far = Vector4.Transform(clip, inverse);
                far /= far.W;

                var origin = new Vector3(near.X, near.Y, near.Z);
                var direction = Vector3.Normalize(new Vector3(far.X, far.Y, far.Z) - origin);
                float closest = float.MaxValue;
                TraceTriangle hit = default;
                bool found = false;
                float margin = 0;

                foreach (var triangle in Scene.Meshes.Triangles)
                {
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
                    hit = triangle;
                    found = true;
                    margin = MathF.Min(u, MathF.Min(v, 1 - u - v));
                }

                if (!found || margin < 0.03f) continue;

                tested++;

                var expected = new Color(hit.Albedo);
                if (pixels[y * target.Width + x] != expected) errors++;
            }

        Console.WriteLine($"Raster frame {Frame}: stable changes={changed}, visibility errors={errors}/{tested}");
        Errors += errors + changed;

        if (Frame != 12) return;
        if (Errors != 0) throw new Exception($"Raster visibility or stability regression: {Errors} mismatches.");

        Console.WriteLine("PASS: raster matches independent triangle intersections and remains stable across frames.");
        Exit();
    }
}
