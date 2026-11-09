using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace Radiant.Testing;

/// <summary>Read linear GPU output so tone mapping cannot hide direction-dependent errors.</summary>
sealed class LightingRegressionWindow : Window
{
    private readonly LightingReferenceScene Scene = new();
    private readonly (int width, int height, int quality, int probeQuality)[] Cases =
    [
        (960, 540, 1, 2), (1024, 640, 1, 2), (640, 1024, 1, 2), (640, 640, 1, 2),
        (960, 540, 0, 2), (960, 540, 2, 2), (800, 600, 1, 2), (1000, 600, 1, 2),
        (960, 540, 1, 0), (960, 540, 1, 1), (960, 540, 1, 3), (640, 1024, 1, 1)
    ];
    private int Frame;
    private int CaseIndex;
    private int BlockerCase = -1;
    private int CalibrationCase = -1;
    private static readonly Color[] CalibrationColors = [Color.White, new Color(128, 64, 192), Color.Black];

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public LightingRegressionWindow()
        : base(new GameLoop(), new() { ShowLoader = false, Title = "HRC lighting regression", Width = 960, Height = 540 })
    {
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(0);
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);

        if (++Frame % 8 != 0) return;

        if (CalibrationCase >= 0)
        {
            CheckCalibration();

            if (++CalibrationCase == CalibrationColors.Length * 4)
            {
                Console.WriteLine("PASS: HRC radial symmetry, output resolution, resize, all render/probe qualities, four shadow directions, RGB transmission, and absolute radiance calibration.");
                Exit();
            }
            else
                SetCalibration();

            return;
        }

        if (BlockerCase >= 0)
        {
            CheckBlocker();

            if (++BlockerCase == 5)
            {
                CalibrationCase = 0;
                SetCalibration();
            }
            else
                Scene.SetBlocker(BlockerCase);

            return;
        }

        CheckLighting();

        if (++CaseIndex == Cases.Length)
        {
            BlockerCase = 0;
            Scene.SetBlocker(BlockerCase);
            return;
        }

        var testCase = Cases[CaseIndex];
        GraphicsDeviceManager.PreferredBackBufferWidth = testCase.width;
        GraphicsDeviceManager.PreferredBackBufferHeight = testCase.height;
        GraphicsDeviceManager.ApplyChanges();
        UDRQuality.Set(testCase.quality);
        SetProbeQuality(testCase.probeQuality);
    }

    /// <summary>Applies the probe quality used by the next lighting regression case.</summary>
    private void SetProbeQuality(int quality)
    {
        var inspector = Scene.ECS.GetSystem<Inspector>();
        var windows = (Dictionary<string, WindowData>)typeof(Inspector)
            .GetField("Windows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(inspector);
        var window = windows["hrcgi"];
        window.Widgets[window.WidgetIndex["quality"]].DropdownCallback(quality);
    }

    /// <summary>Selects the lighting calibration fixture and its reference settings.</summary>
    private void SetCalibration()
    {
        SetProbeQuality(CalibrationCase % 4);
        Scene.SetCalibration(CalibrationColors[CalibrationCase / 4]);
    }

    /// <summary>Checks rendered calibration values against the expected lighting response.</summary>
    private void CheckCalibration()
    {
        // A uniform opaque emitting medium has the same radiance in every
        // direction. Its angular mean must equal its linear emission, not pi/2
        // times that emission. Include black to catch stale merge contents.
        var color = CalibrationColors[CalibrationCase / 4].ToVector3();
        var expected = new Vector3(MathF.Pow(color.X, 2.2f), MathF.Pow(color.Y, 2.2f), MathF.Pow(color.Z, 2.2f));
        var target = Scene.ECS.GetSystem<HRCGI>().GetOutput();
        var pixels = new HalfVector4[target.Width * target.Height];
        target.GetData(pixels);

        float maximumError = 0;

        for (int y = 4; y <= 6; y++)
            for (int x = 4; x <= 6; x++)
            {
                var pixel = pixels[(target.Height * y / 10) * target.Width + target.Width * x / 10].ToVector4().ToNumerics();
                var error = new Vector3(pixel.X, pixel.Y, pixel.Z) - expected;
                maximumError = MathF.Max(maximumError, MathF.Max(MathF.Abs(error.X), MathF.Max(MathF.Abs(error.Y), MathF.Abs(error.Z))));
            }

        if (!float.IsFinite(maximumError) || maximumError > 0.005f)
            throw new Exception($"HRC radiance normalization failed: case {CalibrationCase}, expected {expected}, maximum error {maximumError}.");

        Console.WriteLine($"PASS calibration {CalibrationCase}: expected {expected}, maximum error {maximumError:F6}.");
    }

    /// <summary>Checks that the configured blocker occludes the expected lighting samples.</summary>
    private void CheckBlocker()
    {
        var renderer = Scene.Renderer;
        var target = Scene.ECS.GetSystem<HRCGI>().GetOutput();
        var pixels = new HalfVector4[target.Width * target.Height];
        target.GetData(pixels);

        var center = renderer.VirtualSize / 2;
        var direction = LightingReferenceScene.BlockerDirection(BlockerCase);

        Vector3 Sample(Vector2 worldPosition)
        {
            var screenPosition = renderer.WorldToScreen(worldPosition);
            int pixelX = (int)(screenPosition.X / renderer.ScreenWidth * target.Width);
            int pixelY = (int)(screenPosition.Y / renderer.ScreenHeight * target.Height);
            var value = pixels[pixelY * target.Width + pixelX].ToVector4().ToNumerics();

            return new Vector3(value.X, value.Y, value.Z);
        }

        var blocked = Sample(center + direction * 600);
        var unblocked = Sample(center - direction * 600);

        if (unblocked.X < 0.02f || blocked.X > unblocked.X * 0.05f || blocked.Z > unblocked.Z * 0.05f)
            throw new Exception($"Light leaked through blocker {BlockerCase}: {blocked} versus {unblocked}.");
        if (BlockerCase < 4 && blocked.Y > unblocked.Y * 0.05f) throw new Exception("Opaque blocker transmitted green light.");
        if (BlockerCase == 4 && blocked.Y < unblocked.Y * 0.6f) throw new Exception("Green glass did not transmit green light.");

        Console.WriteLine($"PASS blocker {BlockerCase}: {blocked} versus unblocked {unblocked}.");
    }

    /// <summary>Checks rendered lighting against the current regression case's expected values.</summary>
    private void CheckLighting()
    {
        var renderer = Scene.Renderer;
        var target = Scene.ECS.GetSystem<HRCGI>().GetOutput();
        var pixels = new HalfVector4[target.Width * target.Height];
        target.GetData(pixels);

        var center = renderer.WorldToScreen(renderer.VirtualSize / 2);
        float radius = Vector2.Distance(center,
            renderer.WorldToScreen(renderer.VirtualSize / 2 + new Vector2(LightingReferenceScene.Radius, 0)));
        float maximumSpread = 0;

        for (int ring = 3; ring <= 7; ring += 2)
        {
            float minimum = float.MaxValue;
            float maximum = 0;

            for (int direction = 0; direction < 16; direction++)
            {
                float angle = direction * (global::System.MathF.PI * 2) / 16;
                var screenPosition = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (radius * ring);
                int pixelX = (int)(screenPosition.X / renderer.ScreenWidth * target.Width);
                int pixelY = (int)(screenPosition.Y / renderer.ScreenHeight * target.Height);
                float value = 0;

                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                        value += pixels[(pixelY + offsetY) * target.Width + pixelX + offsetX].ToVector4().ToNumerics().X / 9;
                }

                if (!float.IsFinite(value) || value <= 0) throw new Exception("Nonfinite or missing light outside the emitter.");

                minimum = MathF.Min(minimum, value);
                maximum = MathF.Max(maximum, value);
            }

            float spread = (maximum - minimum) / maximum;
            maximumSpread = MathF.Max(maximumSpread, spread);
            Console.WriteLine($"Case {CaseIndex} {Cases[CaseIndex]}, ring {ring}: min={minimum:F5}, max={maximum:F5}, spread={spread:P1}");
        }

        Directory.CreateDirectory("artifacts/lighting");

        using (var output = File.Create($"artifacts/lighting/case-{CaseIndex}.png"))
            renderer.SceneRT.SaveAsPng(output, renderer.ScreenWidth, renderer.ScreenHeight);

        if (maximumSpread > 0.20f) throw new Exception($"Asymmetric radial light: {maximumSpread:P1} spread at {Cases[CaseIndex]}.");
        if (target.Width != renderer.ScaledWidth || target.Height != renderer.ScaledHeight)
            throw new Exception($"Lighting output must match the scaled viewport: {target.Width}x{target.Height} versus {renderer.ScaledSize}.");
    }
}

/// <summary>Configurable lighting fixture with calibration surfaces and a movable blocker.</summary>
sealed class LightingReferenceScene : Scene
{
    public const float Radius = 100;

    private HRCGI Lighting;
    private Bilinear Upscaler;
    private int Blocker;

    /// <summary>Selects the lighting calibration fixture and its reference settings.</summary>
    public void SetCalibration(Color emission) => ECS.GetSystem<UniformLightingInput>().Emission = emission;

    /// <summary>Returns the direction used to place the lighting-test blocker.</summary>
    public static Vector2 BlockerDirection(int index) => (index % 4) switch
    {
        0 => Vector2.UnitX,
        1 => Vector2.UnitY,
        2 => -Vector2.UnitX,
        _ => -Vector2.UnitY
    };

    /// <summary>Moves or enables the blocker for the next lighting regression case.</summary>
    public void SetBlocker(int index)
    {
        if (index == 0)
        {
            Blocker = ECS.CreateEntity();
            ECS.AddComponent<Transform2D>(Blocker);
            ECS.AddComponent<Rectangle2D>(Blocker);
            ECS.AddComponent<Material>(Blocker);
        }

        var direction = BlockerDirection(index);
        var size = direction.X != 0 ? new Vector2(80, 600) : new Vector2(600, 80);
        ECS.GetComponent<Rectangle2D>(Blocker).Size = size;
        ECS.GetComponent<Transform2D>(Blocker).Position = Renderer.VirtualSize / 2 + direction * 300 - size / 2;
        ECS.GetComponent<Transform2D>(Blocker).Layer = 1;
        ECS.GetComponent<Material>(Blocker).Albedo = index == 4 ? Color.Lime : Color.Black;
        ECS.GetComponent<Material>(Blocker).Emissive = Color.Black;
    }

    /// <inheritdoc/>
    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<Geometry>();
        ECS.AddSystem<UniformLightingInput>();
        Lighting = ECS.AddSystem<HRCGI>();
        Upscaler = ECS.AddSystem<Bilinear>();
        base.SetupECS();
    }

    /// <inheritdoc/>
    public override void SetupScene()
    {
        Renderer.FitWorld(Renderer.VirtualSize / 2, Renderer.VirtualSize);
        LightFactory.CreateLight(ECS, Renderer.VirtualSize / 2, Radius, Color.Black, Color.White, 0);
        Upscaler.SetInputSource(Lighting.GetOutput);
        base.SetupScene();
    }
}

/// <summary>Supply independently controlled emission and absorption for absolute calibration; scene materials normally couple the two for colored emitters.</summary>
[RunAfter(typeof(Geometry))]
[RunBefore(typeof(HRCGI))]
sealed class UniformLightingInput : com.radiant.engine.core.System
{
    public Color? Emission;

    /// <inheritdoc/>
    public override void Update()
    {
        if (Emission is not Color emission) return;

        var geometry = Scene.ECS.GetSystem<Geometry>();
        Renderer.PushTargets();
        Renderer.SetTarget(geometry.EmissiveTexture).Clear(emission);
        Renderer.SetTarget(geometry.AbsorptionTexture).Clear(Color.White);
        Renderer.PopTargets();
    }
}
