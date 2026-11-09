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

/// <summary>Runs udr4 regression checks and records diagnostic frames.</summary>
sealed class UDR4RegressionWindow : Window
{
    private readonly TemporalTestScene Scene = new();
    private int Frame;
    private bool SawMotion;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public UDR4RegressionWindow() : base(new GameLoop(), new() { ShowLoader = false, Title = "UDR4 regression", Width = 320, Height = 180 })
    {
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(0);
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
        Frame++;

        var upscaler = Scene.ECS.GetSystem<UDR4>();
        var output = upscaler.GetOutput();
        var pixels = new HalfVector4[output.Width * output.Height];
        output.GetData(pixels);

        if (pixels.Any(pixel => !Finite(pixel.ToVector4().ToNumerics()))) throw new Exception($"Non-finite UDR4 output at frame {Frame}.");

        // Compare with current geometry, including frames with fast signed movement and camera changes.
        var geometry = Scene.ECS.GetSystem<Geometry>();
        var reference = new Color[geometry.EmissiveTexture.Width * geometry.EmissiveTexture.Height];
        geometry.EmissiveTexture.GetData(reference);

        var motion = new Vector2[reference.Length];
        geometry.TemporalMotionTexture.GetData(motion);
        if (motion.Any(value => MathF.Abs(value.X) > 10)) SawMotion = true;

        if (Frame > 4)
        {
            int incorrect = 0;

            for (int y = 4; y < output.Height - 4; y++)
            {
                for (int x = 4; x < output.Width - 4; x++)
                {
                    int index = y * output.Width + x;
                    bool solid = reference[index].R > 200;
                    // Ignore the reconstruction footprint at the silhouette.
                    bool interior = true;

                    for (int offsetY = -4; offsetY <= 4; offsetY += 4)
                        for (int offsetX = -4; offsetX <= 4; offsetX += 4)
                            interior &= (reference[(y + offsetY) * output.Width + x + offsetX].R > 200) == solid;

                    if (!interior) continue;

                    float value = pixels[index].ToVector4().ToNumerics().X;
                    if (solid ? value < 0.9f : value > 0.05f) incorrect++;
                }
            }

            if (incorrect != 0) throw new Exception($"UDR4 left {incorrect} stale or missing interior pixels at frame {Frame}.");
        }

        if (Frame == 12 && upscaler.HistoryFrames < 8) throw new Exception("Stable input does not accumulate history.");
        if (Frame == 22 && upscaler.HistoryFrames != 1) throw new Exception("Explicit history reset was ignored.");

        if (Frame == 32)
        {
            if (!SawMotion) throw new Exception("Precise motion did not preserve movement beyond the legacy range.");

            Directory.CreateDirectory("artifacts/udr4");

            using var stream = File.Create("artifacts/udr4/temporal-regression.png");
            Scene.Renderer.SceneRT.SaveAsPng(stream, output.Width, output.Height);
            Console.WriteLine("PASS: UDR4 finite startup, accumulation, fast signed motion, disocclusion, camera mapping, history reset, and output resize.");
            Exit();
        }
    }

    /// <summary>Reports whether the sampled image values are finite.</summary>
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);

    /// <inheritdoc/>
    protected override void Update(GameTime gameTime)
    {
        if (Frame == 21) Scene.ECS.GetSystem<UDR4>().ResetHistory();

        if (Frame == 24)
        {
            GraphicsDeviceManager.PreferredBackBufferWidth = 384;
            GraphicsDeviceManager.PreferredBackBufferHeight = 216;
            GraphicsDeviceManager.ApplyChanges();
        }

        base.Update(gameTime);
    }
}

/// <summary>Connects a controlled image source to temporal reconstruction for regression checks.</summary>
sealed class TemporalTestScene : Scene
{
    private int Entity;
    private int Frame;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        UDRQuality.Set(1);
        ECS.AddSystem<Geometry>();

        ECS.AddSystem<TemporalTestSource>();
        ECS.AddSystem<UDR4>();
    }

    /// <inheritdoc/>
    public override void SetupScene()
    {
        var source = ECS.GetSystem<TemporalTestSource>();
        ECS.GetSystem<UDR4>().SetInputSource(() => source.Output);
        Renderer.FitWorld(new Vector2(160, 90), new Vector2(320, 180));
        Entity = LightFactory.CreateLight(ECS, new Vector2(80, 90), 18, Color.White, Color.White);
        ECS.AddComponent<MotionTrackable>(Entity);
    }

    /// <inheritdoc/>
    public override void Update()
    {
        Frame++;

        float x = Frame < 13 ? 80 : Frame < 18 ? 80 + (Frame - 12) * 24 : 200 - (Frame - 18) * 16;
        ref var transform = ref ECS.GetComponent<Transform2D>(Entity);
        transform.Position.X = Math.Clamp(x, 40, 260);
        if (Frame == 20) Renderer.FitWorld(new Vector2(170, 90), new Vector2(320, 180));
    }
}

/// <summary>Produces deterministic moving imagery and motion data for temporal checks.</summary>
[RunAfter(typeof(Geometry))]
[RunBefore(typeof(UDR4))]
sealed class TemporalTestSource : com.radiant.engine.core.System
{
    public RenderTarget2D Output;

    /// <inheritdoc/>
    public override void Update()
    {
        int width = Renderer.ScreenWidth / 2;
        int height = Renderer.ScreenHeight / 2;

        if (Output == null || Output.Width != width || Output.Height != height)
        {
            Output?.Dispose();
            Output = Renderer.CreateRenderTarget(width, height, SurfaceFormat.HalfVector4);
        }

        Renderer.SetTarget(Output);
        Renderer.Blit(Scene.ECS.GetSystem<Geometry>().EmissiveTexture, BlendState.Opaque, SamplerState.LinearClamp);
        Renderer.SetTarget(null);
    }

    /// <inheritdoc/>
    public override void Dispose() => Output?.Dispose();
}
