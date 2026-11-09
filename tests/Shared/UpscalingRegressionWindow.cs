using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.IO;
using System.Linq;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace Radiant.Testing;

/// <summary>Exercises the shared reconstruction selector through real producers, AA, and presentation.</summary>
public sealed class UpscalingRegressionWindow : Window
{
    private readonly Scene Scene;
    private int Frame;
    private int Case;
    private UpscalingMode[] Modes;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public UpscalingRegressionWindow(Scene scene)
        : base(new GameLoop(), new() { Title = "Reconstruction regression", Width = 320, Height = 180 })
    {
        Scene = scene;
        GameLoop.AddScene(scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(0);
    }

    /// <inheritdoc/>
    protected override void Update(GameTime time)
    {
        if (Frame > 0 && Frame % 5 == 0)
        {
            var upscaler = Scene.ECS.GetSystem<Upscaling>();
            Modes ??= upscaler.HrcInput ? Enum.GetValues<UpscalingMode>() : [UpscalingMode.None, UpscalingMode.UDR4];

            if (Case < Modes.Length * 3)
            {
                upscaler.Select(Modes[Case / 3]);
                UDRQuality.Set(Case % 3);
                Scene.ECS.GetSystem<AntiAliasing>().Mode = Case % 2 == 0 ? PostAAMode.FXAA : PostAAMode.SMAA;
                Case++;
            }
            else if (Case == Modes.Length * 3)
            {
                upscaler.Select(UpscalingMode.UDR4);
                UDRQuality.Set(1);
                GraphicsDeviceManager.PreferredBackBufferWidth = 400;
                GraphicsDeviceManager.PreferredBackBufferHeight = 240;
                GraphicsDeviceManager.ApplyChanges();
                Case++;
            }
            else { Console.WriteLine($"PASS: {Scene.GetType().Name}: all supported reconstruction modes/qualities, FXAA/SMAA, Native bypass, re-enable, finite output and resize."); Exit(); }
        }

        base.Update(time);
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        Frame++;

        if (Frame % 5 != 0) return;

        var upscaler = Scene.ECS.GetSystem<Upscaling>();
        var output = upscaler.GetOutput();

        if (output == null || output.IsDisposed) throw new Exception("Reconstruction has no live output.");

        bool bypass = upscaler.Mode == UpscalingMode.None || UDRQuality.ScaleFactor == 100;

        if (bypass && !ReferenceEquals(output, upscaler.Input())) throw new Exception("Native / off must return the source directly.");
        if (!bypass && (output.Width != Scene.Renderer.ScreenWidth || output.Height != Scene.Renderer.ScreenHeight))
            throw new Exception("Reconstruction output does not match the viewport.");

        if (output.Format == SurfaceFormat.HalfVector4)
        {
            var pixels = new HalfVector4[output.Width * output.Height];
            output.GetData(pixels);

            if (pixels.Any(pixel => { var value = pixel.ToVector4().ToNumerics(); return !float.IsFinite(value.X + value.Y + value.Z + value.W); }))
                throw new Exception("Reconstruction contains non-finite pixels.");
            if (!bypass && Scene.GetType().Name == "Rendering2DScene" && !pixels.Any(pixel => pixel.ToVector4().ToNumerics().W < 0.5f))
                throw new Exception("Spatial reconstruction discarded the transparent 2D background.");
        }

        var target = Scene.Renderer.SceneRT;
        var colors = new Color[target.Width * target.Height];
        target.GetData(colors);

        if (!colors.Any(pixel => pixel.R > 12 || pixel.G > 12 || pixel.B > 12)) throw new Exception("Presentation is empty.");

        Directory.CreateDirectory("artifacts/upscaling");

        using var file = File.Create($"artifacts/upscaling/{Scene.GetType().Name}-{upscaler.Mode}-{UDRQuality.Index}-{target.Width}.png");
        target.SaveAsPng(file, target.Width, target.Height);
    }
}
