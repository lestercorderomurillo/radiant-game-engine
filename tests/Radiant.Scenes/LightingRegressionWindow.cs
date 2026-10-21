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

// Read linear GPU output so tone mapping cannot hide direction-dependent errors.
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

    public LightingRegressionWindow()
        : base(new GameLoop(), new() { Title = "HRC lighting regression", Width = 960, Height = 540 })
    {
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(0);
    }

    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
        if (++Frame % 8 != 0) return;

        if (BlockerCase >= 0)
        {
            CheckBlocker();
            if (++BlockerCase == 5)
            {
                Console.WriteLine("PASS: HRC radial symmetry, output resolution, resize, all render/probe qualities, four shadow directions, and RGB transmission.");
                Exit();
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
        var inspector = Scene.ECS.GetSystem<Inspector>();
        var windows = (Dictionary<string, WindowData>)typeof(Inspector)
            .GetField("Windows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(inspector);
        var window = windows["hrcgi"];
        window.Widgets[window.WidgetIndex["quality"]].DropdownCallback(testCase.probeQuality);
    }

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
            var value = pixels[pixelY * target.Width + pixelX].ToVector4();
            return new Vector3(value.X, value.Y, value.Z);
        }

        var blocked = Sample(center + direction * 600);
        var unblocked = Sample(center - direction * 600);
        if (unblocked.X < 0.02f || blocked.X > unblocked.X * 0.05f || blocked.Z > unblocked.Z * 0.05f)
            throw new Exception($"Light leaked through blocker {BlockerCase}: {blocked} versus {unblocked}.");
        if (BlockerCase < 4 && blocked.Y > unblocked.Y * 0.05f)
            throw new Exception("Opaque blocker transmitted green light.");
        if (BlockerCase == 4 && blocked.Y < unblocked.Y * 0.6f)
            throw new Exception("Green glass did not transmit green light.");
        Console.WriteLine($"PASS blocker {BlockerCase}: {blocked} versus unblocked {unblocked}.");
    }

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
                float angle = direction * MathHelper.TwoPi / 16;
                var screenPosition = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (radius * ring);
                int pixelX = (int)(screenPosition.X / renderer.ScreenWidth * target.Width);
                int pixelY = (int)(screenPosition.Y / renderer.ScreenHeight * target.Height);
                float value = 0;

                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                        value += pixels[(pixelY + offsetY) * target.Width + pixelX + offsetX].ToVector4().X / 9;
                }

                if (!float.IsFinite(value) || value <= 0)
                    throw new Exception("Nonfinite or missing light outside the emitter.");
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
        if (maximumSpread > 0.20f)
            throw new Exception($"Asymmetric radial light: {maximumSpread:P1} spread at {Cases[CaseIndex]}.");
        if (target.Width != renderer.ScaledWidth || target.Height != renderer.ScaledHeight)
            throw new Exception($"Lighting output must match the scaled viewport: {target.Width}x{target.Height} versus {renderer.ScaledSize}.");
    }
}

sealed class LightingReferenceScene : Scene
{
    public const float Radius = 100;
    private HRCGI Lighting;
    private Bilinear Upscaler;
    private int Blocker;

    public static Vector2 BlockerDirection(int index) => (index % 4) switch
    {
        0 => Vector2.UnitX,
        1 => Vector2.UnitY,
        2 => -Vector2.UnitX,
        _ => -Vector2.UnitY
    };

    public void SetBlocker(int index)
    {
        if (index == 0)
        {
            Blocker = ECS.CreateEntity();
            ECS.AddComponent<Transform>(Blocker);
            ECS.AddComponent<Rectangle2D>(Blocker);
            ECS.AddComponent<Material>(Blocker);
        }

        var direction = BlockerDirection(index);
        var size = direction.X != 0 ? new Vector2(80, 600) : new Vector2(600, 80);
        ECS.GetComponent<Rectangle2D>(Blocker).Size = size;
        ECS.GetComponent<Transform>(Blocker).Position = new Vector3(Renderer.VirtualSize / 2 + direction * 300 - size / 2, 1);
        ECS.GetComponent<Material>(Blocker).Albedo = index == 4 ? Color.Lime : Color.Black;
        ECS.GetComponent<Material>(Blocker).Emissive = Color.Black;
    }

    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<Geometry>();
        Lighting = ECS.AddSystem<HRCGI>();
        Upscaler = ECS.AddSystem<Bilinear>();
        base.SetupECS();
    }

    public override void SetupScene()
    {
        Renderer.FitWorld(Renderer.VirtualSize / 2, Renderer.VirtualSize);
        LightFactory.CreateLight(ECS, Renderer.VirtualSize / 2, Radius, Color.Black, Color.White, 0);
        Upscaler.SetInputSource(Lighting.GetOutput);
        base.SetupScene();
    }
}
