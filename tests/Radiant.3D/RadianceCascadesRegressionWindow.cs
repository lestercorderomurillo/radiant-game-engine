using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

/// <summary>A closed emissive cube seen from inside: every surface must receive exactly pi times its own radiance.</summary>
internal sealed class FurnaceGeometry : ITraceGeometry
{
    public IReadOnlyList<TraceTriangle> Triangles { get; private set; }
    public long Revision { get; private set; }
    public TraceCamera Camera => new(Matrix.CreateLookAt(new Vector3(0.2f, -0.1f, 0.3f), new Vector3(0.3f, 0.1f, 1), Vector3.UnitY), Matrix.CreatePerspectiveFieldOfView(MathF.PI / 3, 1.5f, 0.05f, 10), new Vector3(0.2f, -0.1f, 0.3f), false, 10);
    public bool HasCamera => true;
    public Point Size => new(96, 64);

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public FurnaceGeometry()
    {
        var triangles = new List<TraceTriangle>();
        var albedo = new Vector3(0.5f);
        var emission = new Vector3(1);

        foreach (int axis in new[] { 0, 1, 2 })
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 corner(float u, float v) => axis switch { 0 => new(side, u, v), 1 => new(u, side, v), _ => new(u, v, side) };
                triangles.Add(new(corner(-1, -1), corner(1, -1), corner(1, 1), albedo, emission));
                triangles.Add(new(corner(-1, -1), corner(1, 1), corner(-1, 1), albedo, emission));
            }

        Triangles = triangles;
    }

    public void SetEmission(float radiance)
    {
        Triangles = Triangles.Select(triangle => triangle with { Emission = new Vector3(radiance) }).ToArray();
        Revision++;
    }

    public void Clear()
    {
        Triangles = [];
        Revision++;
    }
}

/// <summary>Connects the furnace fixture to a radiance cascade stage.</summary>
internal sealed class FurnaceScene : Scene
{
    public RadianceCascades Cascades;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        Cascades = ECS.AddSystem(new RadianceCascades { Geometry = new FurnaceGeometry(), GridSize = 16, BaseDirectionRows = 2, Environment = Vector3.One });
        ECS.AddSystem(new RenderPresentation { Input = Cascades.GetOutput, After = [Cascades] });
    }
}

/// <summary>Runs radiance cascade regression checks on the furnace fixture or the Cornell box.</summary>
internal sealed class RadianceCascadesRegressionWindow : Window
{
    private readonly Scene Scene;
    private readonly bool Furnace;
    private readonly bool Native;
    private readonly int InitialGrid;
    private int ChangedGrid => InitialGrid == 32 ? 16 : 8;
    private int Frame;
    private readonly System.Diagnostics.Stopwatch Clock = new();
    private double TimedMilliseconds;
    private int TimedFrames;
    private float ReferenceLuminance;
    private float BouncedLuminance;
    private long CachedUpdates;
    private Vector4[] CachedPixels;
    private double CachedMilliseconds;
    private double FullHdUpdatingMilliseconds;
    private readonly float[] FencePixel = new float[1];
    private CornellBoxScene Cornell => (CornellBoxScene)Scene;
    private RadianceCascades Cascades => Scene is FurnaceScene furnace ? furnace.Cascades : Cornell.Cascades;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public RadianceCascadesRegressionWindow(bool furnace, bool native = false, bool dense = false) : base(new GameLoop(), new() { ShowLoader = false, Title = "Radiant radiance cascades regression", Width = 640, Height = 360 })
    {
        Furnace = furnace;
        Native = native;
        InitialGrid = dense ? 32 : 16;
        Scene = furnace ? new FurnaceScene() : new CornellBoxScene();
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(0);
    }

    /// <inheritdoc/>
    protected override void Update(GameTime time)
    {
        if (!Furnace && Frame == 2)
        {
            Cornell.SelectRenderer(3);
            if (Cornell.Upscaler.Mode != UpscalingMode.UDR4 || UDRQuality.Index != 1) throw new Exception("Radiance cascades did not default to UDR4 Balanced.");
            if (Native) Cornell.Upscaler.Select(UpscalingMode.None);
            Cascades.GridSize = InitialGrid;
        }
        base.Update(time);

        if (Furnace)
        {
            if (Frame == 14) Cascades.MultiBounce = false;
            if (Frame == 18) { Cascades.MultiBounce = true; Cascades.GridSize = 8; Cascades.BaseDirectionRows = 4; Cascades.IntervalScale = 0.7f; }
            if (Frame == 30) ((FurnaceGeometry)Cascades.Geometry).SetEmission(0.5f);
            if (Frame == 33) Cascades.Environment = new Vector3(0.2f);
            if (Frame == 36) Cascades.NormalBias = 0.2f;
            if (Frame == 39) Cascades.MaxCascades = 1;
            if (Frame == 42) Cascades.MaxCascades = 4;
            if (Frame == 45) ((FurnaceGeometry)Cascades.Geometry).Clear();
            if (Frame == 48) Cascades.Geometry = new FurnaceGeometry();
            if (Frame == 51) Cascades.LeakFix = false;
            if (Frame == 54) { Cascades.LeakFix = true; Cascades.ResetBounces(); }
            if (Frame == 57) Cascades.Volume = (Vector3.Zero, 2);
            if (Frame == 60) Cascades.Volume = null;
            if (Frame == 96) ((FurnaceGeometry)Cascades.Geometry).SetEmission(0.5f);

            return;
        }

        if (Frame == 24) Cascades.MultiBounce = false;
        if (Frame == 28) { Cascades.MultiBounce = true; Cascades.GridSize = ChangedGrid; }

        if (Frame == 32)
        {
            GraphicsDeviceManager.PreferredBackBufferWidth = 800;
            GraphicsDeviceManager.PreferredBackBufferHeight = 500;
            GraphicsDeviceManager.ApplyChanges();
        }

        if (Frame == 40) Cornell.Meshes.CameraEntity = int.MaxValue;
        if (Frame == 43) Cornell.Meshes.CameraEntity = -1;
        if (Frame == 47) Cornell.SelectRenderer(0);
        if (Frame == 50) Cornell.SelectRenderer(3);
        if (Frame == 56)
        {
            Cascades.GridSize = InitialGrid;
            Cascades.ResetBounces();
            GraphicsDeviceManager.PreferredBackBufferWidth = 640;
            GraphicsDeviceManager.PreferredBackBufferHeight = 360;
            GraphicsDeviceManager.ApplyChanges();
        }
        if (Frame == 98) Scene.ECS.Query((int thread, int entity, ref Camera3D camera) => camera.Position += new Vector3(0.25f, 0, 0));
        if (Frame == 100) Cascades.Environment *= 0.5f;
        if (Frame == 102) Cascades.ResetBounces();
        if (Frame == 104) Cascades.MaxBounceUpdates = 1;
        if (Frame == 106) Cascades.MaxBounceUpdates = 0;
        if (Frame == 108) Cornell.Upscaler.Select(UpscalingMode.None);
        if (Frame == 110) { Cornell.Upscaler.Select(UpscalingMode.UDR4); UDRQuality.Set(1); }
        if (Frame == 112) UDRQuality.Set(2);
        if (Frame == 114) UDRQuality.Set(1);
        if (Frame == 116)
        {
            Cascades.MaxBounceUpdates = 32;
            Cascades.ResetBounces();
            GraphicsDeviceManager.PreferredBackBufferWidth = 1920;
            GraphicsDeviceManager.PreferredBackBufferHeight = 1080;
            GraphicsDeviceManager.ApplyChanges();
        }
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        Frame++;

        if (Furnace) DrawFurnace(); else DrawCornell();
    }

    /// <summary>Checks furnace normalization and bounce convergence against the analytic series 2 - 0.5^n.</summary>
    private void DrawFurnace()
    {
        if (Frame is 1 or 2 or 12 or 16 or 20 or 30 or 31 or 34 or 37 or 40 or 43 or 49 or 55 or 58 or 61 or 97)
        {
            var pixels = Read(Cascades.GetOutput());
            float minimum = pixels.Min(pixel => Math.Min(pixel.X, Math.Min(pixel.Y, pixel.Z)));
            float maximum = pixels.Max(pixel => Math.Max(pixel.X, Math.Max(pixel.Y, pixel.Z)));

            if (pixels.Any(pixel => !float.IsFinite(pixel.X + pixel.Y + pixel.Z + pixel.W) || pixel.W != 1)) throw new Exception("Furnace produced non-finite or transparent pixels.");

            // Frame 1 sees only emission plus one bounce; feedback then adds 0.5^n per frame.
            float expected = Frame switch
            {
                1 or 16 or 49 or 55 or 58 or 61 => 1.5f,
                2 or 20 => 1.75f,
                31 or 34 or 37 or 40 or 43 or 97 => 0.75f,
                _ => 2f
            };
            float tolerance = Frame is 12 or 30 ? 0.03f : 0.02f;

            if (Math.Abs(minimum - expected) > tolerance || Math.Abs(maximum - expected) > tolerance)
                throw new Exception($"Furnace frame {Frame} produced [{minimum:F4}, {maximum:F4}], expected {expected} (cascades={Cascades.CascadeCount}, bounce frames={Cascades.BounceFrames}).");

            Console.WriteLine($"PASS: furnace frame {Frame}: [{minimum:F4}, {maximum:F4}] for expected {expected}, grid {Cascades.GridSize}, {Cascades.CascadeCount} cascades, {Cascades.RaysPerFrame} rays.");
        }

        if (Frame == 19 && Cascades.BounceFrames != 1) throw new Exception("Changing the probe grid did not restart bounce feedback.");
        if (Frame is 31 or 34 or 37 or 40 or 43 or 49 or 52 or 55 or 58 or 61 or 97 && Cascades.BounceFrames != 1) throw new Exception($"Frame {Frame} retained stale bounce feedback.");
        if (Frame == 40 && Cascades.CascadeCount != 1) throw new Exception("Lowering MaxCascades did not rebuild the layout.");
        if (Frame == 43 && Cascades.CascadeCount != 4) throw new Exception("Raising MaxCascades did not restore the layout.");
        if (Frame == 52)
        {
            // Uncorrected interpolation can leak the darker environment through the enclosure.
            // It must still remain finite and within the emission/direct-light energy bounds.
            if (Read(Cascades.GetOutput()).Any(pixel => !float.IsFinite(pixel.X + pixel.Y + pixel.Z) || pixel.W != 1 || pixel.X < 1 || pixel.X > 1.51f)) throw new Exception("Uncorrected merging produced invalid radiance.");
        }
        if (Frame == 46)
        {
            if (Read(Cascades.GetOutput()).Any(pixel => pixel != Vector4.Zero) || Cascades.BounceFrames != 0) throw new Exception("Empty geometry retained stale lighting.");
            var depths = new float[Cascades.DepthTexture.Width * Cascades.DepthTexture.Height];
            Cascades.DepthTexture.GetData(depths);
            if (depths.Any(depth => depth != 0)) throw new Exception("Empty geometry retained stale depth.");
        }
        if (Frame == 61)
        {
            if (Cascades.BvhBuildCount != 4) throw new Exception($"Unexpected BVH build count: {Cascades.BvhBuildCount}.");
            Console.WriteLine("PASS: source revisions/replacement, empty geometry, environment, bias, cascade limits, merge modes and explicit/automatic volume.");
        }
        if (Frame == 94)
        {
            if (!Cascades.ReusedProbeField) throw new Exception("Furnace did not cache its static field.");
            CachedUpdates = Cascades.ProbeUpdateCount;
        }
        if (Frame == 97)
        {
            if (Cascades.ReusedProbeField || Cascades.ProbeUpdateCount != CachedUpdates + 1 || Cascades.BvhBuildCount != 5) throw new Exception("Geometry revision did not refresh cached furnace lighting.");
            Console.WriteLine("PASS: changing emission invalidates an already cached field immediately.");
            Exit();
        }
    }

    /// <summary>Checks Cornell-box lighting, bleeding, bounces, grid changes, resize, camera loss and renderer switching.</summary>
    private void DrawCornell()
    {
        if (Frame == 2 && (Cascades.GetOutput() != null || Cascades.BvhBuildCount != 0)) throw new Exception("Inactive radiance cascades allocated resources.");
        if (Frame == 3)
        {
            if (Cascades.BounceFrames != 1) throw new Exception("The single-bounce baseline was not captured on the first cascade frame.");
            ReferenceLuminance = Luminance(Sample(Read(Cascades.GetOutput()), Cascades.GetOutput(), new Vector3(0, 0, 0.5f)));
        }

        // Whole-frame wall time includes the demo pipeline and presentation; this is not a GPU timestamp.
        if (Frame == 8) { DrainGpu(); Clock.Restart(); }
        if (Frame == 20) { DrainGpu(); TimedMilliseconds = Clock.Elapsed.TotalMilliseconds; TimedFrames = Frame - 8; }

        if (Frame == 6)
        {
            var output = Cascades.GetOutput();

            int scale = Native ? 1 : 2;
            if (output.Width != 640 / scale || output.Height != 360 / scale || Cascades.DepthTexture.Width != 640 / scale) throw new Exception("Radiance cascade output does not match the scaled view.");
            if (Cornell.Upscaler.GetOutput().Width != 640 || Cornell.Upscaler.GetOutput().Height != 360) throw new Exception("UDR did not reconstruct at display resolution.");
            if (Cascades.CascadeCount < 3 || Cascades.BvhBuildCount != 1) throw new Exception($"Unexpected cascade setup: {Cascades.CascadeCount} cascades, {Cascades.BvhBuildCount} builds.");

            var pixels = Read(output);

            if (pixels.Any(pixel => !float.IsFinite(pixel.X + pixel.Y + pixel.Z + pixel.W) || pixel.W != 1)) throw new Exception("Radiance cascades produced non-finite or transparent pixels.");
            if (pixels.Select(pixel => new com.radiant.engine.math.Color(pixel)).Distinct().Count() < 16) throw new Exception("Radiance cascade output is uniform.");

            var background = pixels[2 * output.Width + 2];

            if (Math.Abs(background.X - Cascades.Environment.X) > 0.01f) throw new Exception($"Background did not return the environment radiance: {background}.");

            var redSide = Sample(pixels, output, new Vector3(-2.6f, 0, 1));
            var greenSide = Sample(pixels, output, new Vector3(2.6f, 0, 1));
            var center = Sample(pixels, output, new Vector3(0, 0, 0.5f));
            var depths = new float[output.Width * output.Height];
            Cascades.DepthTexture.GetData(depths);
            int centerIndex = Index(output, new Vector3(0, 0, 0.5f));

            if (redSide.X / Math.Max(redSide.Y, 1e-4f) < greenSide.X / Math.Max(greenSide.Y, 1e-4f) * 1.1f) throw new Exception($"No color bleeding from the red wall: {redSide} vs {greenSide}.");
            if (greenSide.Y / Math.Max(greenSide.X, 1e-4f) < redSide.Y / Math.Max(redSide.X, 1e-4f) * 1.1f) throw new Exception($"No color bleeding from the green wall: {greenSide} vs {redSide}.");
            if (Luminance(center) < 0.05f) throw new Exception($"Floor under the emitter is dark: {center}.");
            if (depths[centerIndex] <= 0.1f || depths[centerIndex] >= 1) throw new Exception($"Linear depth is invalid: {depths[centerIndex]}.");

            Save("cornell", output);
        }

        if (Frame == 22)
        {
            BouncedLuminance = Luminance(Sample(Read(Cascades.GetOutput()), Cascades.GetOutput(), new Vector3(0, 0, 0.5f)));

            if (BouncedLuminance < ReferenceLuminance * 1.03f) throw new Exception($"Bounce feedback did not add energy: {ReferenceLuminance} -> {BouncedLuminance}.");
            if (Cascades.BounceFrames < 15) throw new Exception("Bounce frames did not accumulate.");

            Save("cornell-bounced", Cascades.GetOutput());
        }

        if (Frame == 27)
        {
            float single = Luminance(Sample(Read(Cascades.GetOutput()), Cascades.GetOutput(), new Vector3(0, 0, 0.5f)));

            if (Cascades.BounceFrames != 0 || !Cascades.ReusedProbeField || Math.Abs(single - ReferenceLuminance) > ReferenceLuminance * 0.03f) throw new Exception($"Disabling bounces did not return to cached single-bounce lighting: {single} vs {ReferenceLuminance}.");
        }

        if (Frame == 31)
        {
            if (Cascades.GridSize != ChangedGrid || Cascades.BounceFrames > 3 || Math.Abs(Cascades.ProbeSpacing * ChangedGrid - Cascades.VolumeSize) > 1e-4f) throw new Exception("Grid change did not rebuild the cascades.");
            if (Read(Cascades.GetOutput()).Any(pixel => !float.IsFinite(pixel.X + pixel.Y + pixel.Z))) throw new Exception("Coarse grid produced non-finite pixels.");
        }

        if (Frame == 38 && (Cascades.GetOutput().Width != (Native ? 800 : 400) || Cascades.DepthTexture.Height != (Native ? 500 : 250) || Cornell.Upscaler.GetOutput().Width != 800)) throw new Exception("Radiance cascades / UDR did not follow the resize.");
        if (Frame == 42 && Read(Cascades.GetOutput()).Any(pixel => pixel.W != 0)) throw new Exception("Missing camera retained a stale radiance cascade image.");
        if (Frame == 46 && Read(Cascades.GetOutput()).Any(pixel => pixel.W != 1)) throw new Exception("Restored camera did not resume rendering.");
        if (Frame == 49 && (Cascades.GetOutput() != null || Cascades.DepthTexture != null || Cascades.Buffers["Cascade 0"]() != null)) throw new Exception("Switching renderers retained radiance cascade resources.");

        if (Frame == 56)
        {
            if (Cascades.GetOutput() == null || Read(Cascades.GetOutput()).Any(pixel => pixel.W != 1)) throw new Exception("Radiance cascades did not resume after switching back.");

            Console.WriteLine($"PASS: Cornell radiance cascades (grid {InitialGrid}, {(Native ? "native" : "UDR4 Balanced")}, {TimedMilliseconds / Math.Max(TimedFrames, 1):F2} ms/frame while updating): bleeding, environment, depth, bounces, grid change, resize, camera loss and switching.");
        }

        if (Frame == 90)
        {
            if (!Cascades.ReusedProbeField || Cascades.BounceFrames != Cascades.MaxBounceUpdates) throw new Exception("Static lighting did not stop at the configured bounce budget.");
            CachedUpdates = Cascades.ProbeUpdateCount;
            CachedPixels = Read(Cascades.GetOutput());
            Clock.Restart();
        }
        if (Frame == 98)
        {
            DrainGpu();
            CachedMilliseconds = Clock.Elapsed.TotalMilliseconds / 8;
            if (Cascades.ProbeUpdateCount != CachedUpdates || !Read(Cascades.GetOutput()).SequenceEqual(CachedPixels)) throw new Exception("Reusing the probe field changed static lighting.");
            Save("cornell-optimized", Cornell.Upscaler.GetOutput());
        }
        if (Frame == 100 && (Cascades.ProbeUpdateCount != CachedUpdates || Read(Cascades.GetOutput()).SequenceEqual(CachedPixels))) throw new Exception("Moving the camera did not reuse world-space lighting or update primary shading.");
        if (Frame == 101 && (Cascades.ProbeUpdateCount != CachedUpdates + 1 || Cascades.BounceFrames != 1 || Cascades.ReusedProbeField)) throw new Exception("Environment change retained cached lighting.");
        if (Frame == 103 && (Cascades.BounceFrames != 1 || Cascades.ReusedProbeField)) throw new Exception("Explicit reset retained cached lighting.");
        if (Frame == 106 && !Cascades.ReusedProbeField) throw new Exception("Lowering the update budget did not reuse the field.");
        if (Frame == 108 && Cascades.ReusedProbeField) throw new Exception("Unlimited bounce updates did not resume.");
        if (Frame == 110 && Cascades.GetOutput().Width != 640) throw new Exception("Turning UDR off did not restore native rendering.");
        if (Frame == 112 && (Cascades.GetOutput().Width != 320 || Cornell.Upscaler.GetOutput().Width != 640)) throw new Exception("Re-enabling UDR did not reduce rendering and reconstruct output.");
        if (Frame == 114 && (Cascades.GetOutput().Width != 640 || !ReferenceEquals(Cornell.Upscaler.GetOutput(), Cornell.Upscaler.Input()))) throw new Exception("UDR Native did not bypass reconstruction.");
        if (Frame == 116)
        {
            if (Cascades.GetOutput().Width != 320 || Read(Cornell.Upscaler.GetOutput()).Any(pixel => !float.IsFinite(pixel.X + pixel.Y + pixel.Z))) throw new Exception("Balanced output is invalid.");
            Console.WriteLine($"PASS: field reuse, camera motion, invalidation, bounce budget and UDR off/on/Native/Balanced; {CachedMilliseconds:F2} ms/frame cached at 640x360, grid {InitialGrid}, {(Native ? "native" : "UDR4 Balanced")}.");
        }
        if (Frame == 120)
        {
            if (Cascades.GetOutput().Width != 960 || Cascades.GetOutput().Height != 540 || Cornell.Upscaler.GetOutput().Width != 1920 || Cornell.Upscaler.GetOutput().Height != 1080) throw new Exception("Full HD Balanced did not use a half-resolution producer.");
            DrainGpu();
            Clock.Restart();
        }
        if (Frame == 132) { DrainGpu(); FullHdUpdatingMilliseconds = Clock.Elapsed.TotalMilliseconds / 12; }
        if (Frame == 152)
        {
            if (!Cascades.ReusedProbeField) throw new Exception("Full HD field did not settle.");
            DrainGpu();
            Clock.Restart();
        }
        if (Frame == 160)
        {
            DrainGpu();
            double cachedMilliseconds = Clock.Elapsed.TotalMilliseconds / 8;
            Save("cornell-optimized-1080p", Cornell.Upscaler.GetOutput());
            Console.WriteLine($"PASS: 1920x1080 UDR4 Balanced, grid {InitialGrid}: {FullHdUpdatingMilliseconds:F2} ms/frame updating, {cachedMilliseconds:F2} ms/frame cached (GPU-completed wall time).");
            Exit();
        }
    }

    /// <summary>Projects a world point into the current output and returns its pixel index.</summary>
    private int Index(Texture2D output, Vector3 world)
    {
        var camera = Cornell.Meshes.Camera;
        var clip = Vector4.Transform(new Vector4(world, 1), camera.View * camera.Projection);
        int x = Math.Clamp((int)((clip.X / clip.W + 1) * 0.5f * output.Width), 0, output.Width - 1);
        int y = Math.Clamp((int)((1 - clip.Y / clip.W) * 0.5f * output.Height), 0, output.Height - 1);

        return y * output.Width + x;
    }

    /// <summary>Averages a 5x5 pixel block around a projected world point.</summary>
    private Vector4 Sample(Vector4[] pixels, Texture2D output, Vector3 world)
    {
        int index = Index(output, world);
        var total = Vector4.Zero;

        for (int y = -2; y <= 2; y++) for (int x = -2; x <= 2; x++) total += pixels[Math.Clamp(index + y * output.Width + x, 0, pixels.Length - 1)];

        return total / 25;
    }

    private static float Luminance(Vector4 pixel) => 0.2126f * pixel.X + 0.7152f * pixel.Y + 0.0722f * pixel.Z;

    /// <summary>Bounds timing samples with GPU completion so queued submissions cannot masquerade as faster frames.</summary>
    private void DrainGpu() => Cascades.DepthTexture.GetData(0, new(0, 0, 1, 1), FencePixel, 0, 1);

    /// <summary>Reads half-float or float render-target pixels for regression assertions.</summary>
    private static Vector4[] Read(Texture2D texture)
    {
        if (texture.Format == com.radiant.engine.graphics.SurfaceFormat.Color)
        {
            var colors = new com.radiant.engine.math.Color[texture.Width * texture.Height];
            texture.GetData(colors);
            return colors.Select(color => color.ToVector4()).ToArray();
        }
        if (texture.Format == com.radiant.engine.graphics.SurfaceFormat.HalfVector4)
        {
            var data = new HalfVector4[texture.Width * texture.Height];
            texture.GetData(data);
            return data.Select(pixel => pixel.ToVector4().ToNumerics()).ToArray();
        }

        var values = new Vector4[texture.Width * texture.Height];
        texture.GetData(values);
        return values;
    }

    /// <summary>Writes the current test image to its diagnostic output path.</summary>
    private void Save(string suffix, Texture2D texture)
    {
        Directory.CreateDirectory("artifacts/radiance-cascades");

        using var stream = File.Create($"artifacts/radiance-cascades/{suffix}.png");
        using var preview = Scene.Renderer.CreateTexture(texture.Width, texture.Height);
        preview.SetData(Read(texture).Select(pixel => new com.radiant.engine.math.Color(Vector4.Clamp(pixel, Vector4.Zero, Vector4.One))).ToArray());
        preview.SaveAsPng(stream, preview.Width, preview.Height);
    }
}
