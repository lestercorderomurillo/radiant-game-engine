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
    private Func<Texture2D> OriginalInput;
    private bool OriginalHrcInput;
    private bool OriginalGuidance;
    private bool OriginalLightingEnabled;
    private bool TestingGuidance;
    private Color? ExpectedColor;
    private static readonly Color[] ImageColors = [new(48, 24, 12, 64), new(24, 96, 48, 128), Color.Transparent];

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public UpscalingRegressionWindow(Scene scene)
        : base(new GameLoop(), new() { ShowLoader = false, Title = "Reconstruction regression", Width = 320, Height = 180 })
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
            Modes ??= Enum.GetValues<UpscalingMode>();
            if (OriginalInput == null)
            {
                OriginalInput = upscaler.Input;
                OriginalHrcInput = upscaler.HrcInput;
                OriginalGuidance = upscaler.UseGeometryGuidance;
                OriginalLightingEnabled = Scene.ECS.GetSystem<HRCGI>()?.Enabled == true;
            }

            if (Case < Modes.Length * 3)
            {
                upscaler.Select(Modes[Case / 3]);
                UDRQuality.Set(Case % 3);
                Scene.ECS.GetSystem<AntiAliasing>().Mode = Case % 2 == 0 ? PostAAMode.FXAA : PostAAMode.SMAA;
                Case++;
            }
            else if (Case < Modes.Length * 3 + 4 * ImageColors.Length)
            {
                // Arbitrary RGBA images must work even when unrelated HRC geometry exists in the scene.
                int imageCase = Case - Modes.Length * 3;
                upscaler.Select(UpscalingMode.None);
                upscaler.HrcInput = false;
                upscaler.UseGeometryGuidance = false;
                ExpectedColor = ImageColors[imageCase % ImageColors.Length];
                var image = Scene.Renderer.GetSolidTexture(ExpectedColor.Value);
                upscaler.Input = () => image;
                upscaler.Select(Modes[1 + imageCase / ImageColors.Length]);
                UDRQuality.Set(1);
                Case++;
            }
            else if (Case < Modes.Length * 3 + 4 * ImageColors.Length + 4)
            {
                // Guidance is independent of HRC; absent guidance still permits every algorithm.
                upscaler.Select(UpscalingMode.None);
                upscaler.HrcInput = false;
                upscaler.UseGeometryGuidance = true;
                ExpectedColor = null;
                TestingGuidance = true;
                var geometry = Scene.ECS.GetSystem<Geometry>();
                var lighting = Scene.ECS.GetSystem<HRCGI>();
                if (lighting != null) lighting.Enabled = false;
                upscaler.Input = geometry != null ? () => geometry.EmissiveTexture : OriginalInput;
                upscaler.Select(Modes[1 + Case - Modes.Length * 3 - 4 * ImageColors.Length]);
                UDRQuality.Set(1);
                Case++;
            }
            else if (Case == Modes.Length * 3 + 4 * ImageColors.Length + 4)
            {
                upscaler.Select(UpscalingMode.None);
                upscaler.Input = OriginalInput;
                upscaler.HrcInput = OriginalHrcInput;
                upscaler.UseGeometryGuidance = OriginalGuidance;
                ExpectedColor = null;
                TestingGuidance = false;
                var lighting = Scene.ECS.GetSystem<HRCGI>();
                if (lighting != null) lighting.Enabled = OriginalLightingEnabled;
                upscaler.Select(UpscalingMode.UDR4);
                UDRQuality.Set(1);
                GraphicsDeviceManager.PreferredBackBufferWidth = 400;
                GraphicsDeviceManager.PreferredBackBufferHeight = 240;
                GraphicsDeviceManager.ApplyChanges();
                Case++;
            }
            else { Console.WriteLine($"PASS: {Scene.GetType().Name}: all reconstruction modes/qualities, arbitrary RGBA input, optional guidance without HRC, FXAA/SMAA, Native bypass, re-enable, finite output and resize."); Exit(); }
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
        if (TestingGuidance && Scene.ECS.GetSystem<HRCGI>() is { } lighting && lighting.OutputJitter != Vector2.Zero)
            throw new Exception("Geometry-only guidance must not jitter an unrelated lighting producer.");

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
            if (!bypass && ExpectedColor == null && Scene.GetType().Name == "Rendering2DScene" && !pixels.Any(pixel => pixel.ToVector4().ToNumerics().W < 0.5f))
                throw new Exception("Spatial reconstruction discarded the transparent 2D background.");
            if (ExpectedColor is { } expected)
            {
                var expectedValue = expected.ToVector4();
                if (pixels.Any(pixel => Vector4.Distance(pixel.ToVector4().ToNumerics(), expectedValue) > 0.015f))
                    throw new Exception($"{upscaler.Mode} changed a uniform RGBA image or used unrelated geometry/history.");
            }
        }

        var target = Scene.Renderer.SceneRT;
        var colors = new Color[target.Width * target.Height];
        target.GetData(colors);

        if (ExpectedColor == null && !colors.Any(pixel => pixel.R > 12 || pixel.G > 12 || pixel.B > 12)) throw new Exception("Presentation is empty.");

        Directory.CreateDirectory("artifacts/upscaling");

        using var file = File.Create($"artifacts/upscaling/{Scene.GetType().Name}-{upscaler.Mode}-{UDRQuality.Index}-{target.Width}.png");
        target.SaveAsPng(file, target.Width, target.Height);
    }
}
