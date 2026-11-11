using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics.PackedVector;

/// <summary>Exports the actual Cornell geometry and native linear cascade output for independent offline comparison.</summary>
internal sealed class CascadeReferenceWindow : Window
{
    private readonly CornellBoxScene Scene = new() { InitialRenderMethod = 3 };
    private int Frame;

    public CascadeReferenceWindow() : base(new GameLoop(), new() { ShowLoader = false, Title = "Cascade reference", Width = 1280, Height = 720 })
    {
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(0);
    }

    protected override void Update(GameTime time)
    {
        base.Update(time);
        Scene.Upscaler.Select(UpscalerMode.None);
        Scene.Cascades.MaxBounceUpdates = 64;
    }

    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        if (++Frame != 70) return;

        const string directory = "artifacts/radiance-reference";
        Directory.CreateDirectory(directory);
        var camera = Scene.Meshes.Camera;
        Matrix.Invert(camera.View, out var inverse);
        float[] vector(Vector3 value) => [value.X, value.Y, value.Z];
        var target = Scene.Cascades.GetOutput();
        File.WriteAllText($"{directory}/scene.json", JsonSerializer.Serialize(new
        {
            width = target.Width, height = target.Height,
            position = vector(camera.Position),
            forward = vector(Vector3.TransformNormal(-Vector3.UnitZ, inverse)),
            up = vector(Vector3.TransformNormal(Vector3.UnitY, inverse)),
            fov = 2 * MathF.Atan(1 / camera.Projection.M22) * 180 / MathF.PI,
            environment = vector(Scene.Cascades.Environment),
            triangles = Scene.Meshes.Triangles.Select(t => new { a = vector(t.A), b = vector(t.B), c = vector(t.C), albedo = vector(t.Albedo), emission = vector(t.Emission) })
        }));
        var pixels = new HalfVector4[target.Width * target.Height];
        target.GetData(pixels);
        if (pixels.Any(p => !float.IsFinite(p.ToVector4().X + p.ToVector4().Y + p.ToVector4().Z)))
            throw new Exception("Cascade reference capture contains non-finite radiance.");
        using (var writer = new BinaryWriter(File.Create($"{directory}/cascade.f32")))
            foreach (var pixel in pixels)
            {
                var value = pixel.ToVector4();
                writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z);
            }
        using var preview = Scene.Renderer.CreateTexture(target.Width, target.Height);
        preview.SetData(pixels.Select(p => new com.radiant.engine.math.Color(Vector4.Clamp(p.ToVector4().ToNumerics(), Vector4.Zero, Vector4.One))).ToArray());
        using var stream = File.Create($"{directory}/cascade.png");
        preview.SaveAsPng(stream, target.Width, target.Height);
        Console.WriteLine($"CAPTURE: native {target.Width}x{target.Height}, {Scene.Cascades.BounceFrames} bounce updates, exported geometry and linear RGB.");
        Exit();
    }
}
