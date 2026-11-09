using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.IO;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics.PackedVector;

/// <summary>High-contrast geometry fixture for raster-edge and resolve checks.</summary>
internal sealed class RasterEdgesScene : Scene
{
    public Geometry3D Geometry;
    public RasterRenderer Raster;
    public RasterSampling Sampling;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        Sampling = ECS.AddSystem(new RasterSampling { Mode = RasterAAMode.MSAA4x });

        var meshes = ECS.AddSystem(new MeshGeometry3D { Sampling = Sampling });
        Geometry = ECS.AddSystem(new Geometry3D { Source = meshes, Channels = GeometryChannels.Color | GeometryChannels.Normals, After = [meshes] });
        Raster = ECS.AddSystem(new RasterRenderer { Geometry = Geometry, Input = Geometry.GetOutput, LightDirection = Vector3.Normalize(new Vector3(0, 1, 1)), Ambient = 0.1f, After = [Geometry] });
        ECS.AddSystem(new RenderPresentation { Input = Raster.GetOutput, After = [Raster] });
        base.SetupECS();
    }

    /// <inheritdoc/>
    public override void SetupScene()
    {
        int camera = ECS.CreateEntity();
        ECS.AddComponent<Camera3D>(camera) = new Camera3D { Position = new Vector3(4, 3, 6), Target = Vector3.Zero, FarPlane = 40 };

        int cube = ECS.CreateEntity(Vector3.Zero);
        ECS.GetComponent<Transform3D>(cube).Scale = new Vector3(3);
        ECS.AddComponent<Mesh3D>(cube) = new Mesh3D { Color = Color.White };
    }
}

/// <summary>Runs raster edges checks and records diagnostic frames.</summary>
internal sealed class RasterEdgesWindow : Window
{
    private readonly RasterEdgesScene Scene = new();
    private int Frame;
    private readonly RasterAAMode[] Cases = [RasterAAMode.MSAA2x, RasterAAMode.MSAA4x, RasterAAMode.MSAA8x, RasterAAMode.None, RasterAAMode.SSAA2x, RasterAAMode.SSAA3x];
    private HalfVector4[] Previous;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public RasterEdgesWindow() : base(new GameLoop(), new() { ShowLoader = false, Title = "Radiant raster edge regression", Width = 640, Height = 360 }) { GameLoop.AddScene(Scene); GameLoop.ThrottleUnfocused = false; }

    /// <inheritdoc/>
    protected override void Update(GameTime time)
    {
        base.Update(time);

        if (Frame % 8 == 0 && Frame / 8 < Cases.Length) { Scene.Sampling.Mode = Cases[Frame / 8]; Previous = null; }
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        Frame++;

        if (Frame % 8 != 7 && Frame % 8 != 0) return;

        var output = Scene.Raster.GetOutput();
        var normals = new Color[output.Width * output.Height];
        Scene.Geometry.NormalTexture.GetData(normals);

        var pixels = new HalfVector4[normals.Length];
        output.GetData(pixels);

        if (Frame % 8 == 7) { Previous = pixels; return; }

        for (int i = 0; i < pixels.Length; i++) if (pixels[i].PackedValue != Previous[i].PackedValue) throw new Exception("Static raster lighting flickers between frames.");

        // Cube +Y and +Z faces have identical n dot L = 1/sqrt(2).
        // Every mixture of those samples must retain the same brightness at their shared edge.
        float expected = 0.1f + 1 / MathF.Sqrt(2), maximumError = 0;
        int edges = 0;
        bool Valid(Color n) => n.A == 255 && Math.Abs(n.R - 128) <= 1 && n.G >= 127 && n.B >= 127 && Math.Abs(n.G + n.B - 383) <= 2;

        for (int y = 1; y < output.Height - 1; y++)
            for (int x = 1; x < output.Width - 1; x++)
            {
                int i = y * output.Width + x;

                if (!Valid(normals[i])) continue;

                bool interior = true, up = false, front = false;

                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { var n = normals[i + dy * output.Width + dx]; interior &= Valid(n); up |= n.G > 140; front |= n.B > 140; }

                if (!interior || !up || !front) continue;

                var color = pixels[i].ToVector4().ToNumerics();
                maximumError = Math.Max(maximumError, Math.Max(Math.Abs(color.X - expected), Math.Max(Math.Abs(color.Y - expected), Math.Abs(color.Z - expected))));
                edges++;
            }

        Directory.CreateDirectory("artifacts/raster-edges");

        using (var stream = File.Create($"artifacts/raster-edges/{Scene.Sampling.Mode}.png")) output.SaveAsPng(stream, output.Width, output.Height);

        Console.WriteLine($"Raster {Scene.Sampling.Mode} (lit {((RenderTarget2D)output).MultiSampleCount}x / buffers {Scene.Geometry.ColorTexture.MultiSampleCount}x): {edges} edge pixels, maximum brightness error={maximumError:F6}");

        if (edges < 20 || maximumError > 0.005f) throw new Exception("Raster MSAA introduced a bright seam between equally lit faces.");

        Console.WriteLine($"PASS: {Scene.Sampling.Mode} preserves constant lighting across hard-normal edges without flicker.");
        if (Frame == Cases.Length * 8) Exit();
    }
}
