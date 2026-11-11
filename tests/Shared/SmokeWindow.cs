using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.IO;
using System.Linq;
using com.radiant.engine.core;
using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;

namespace Radiant.Testing;

/// <summary>Exercises the real window, content pipeline, GPU, resize path, and disposal.</summary>
public sealed class SmokeWindow : Window
{
    private readonly Scene Scene;
    private readonly string Output;
    private readonly Action<Scene, int> Step;
    private readonly Action<Scene, int> BeforeDraw;
    private int Frame;
    private readonly int ResizeWidth, ResizeHeight;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public SmokeWindow(Scene Scene, string Output, Action<Scene, int> Step = null, Action<Scene, int> BeforeDraw = null, int Width = 960, int Height = 540, int ResizeWidth = 1024, int ResizeHeight = 640)
        : base(new GameLoop(), new() { ShowLoader = false, Title = "Radiant smoke test", Width = Width, Height = Height })
    {
        this.Scene = Scene;
        this.Output = Output;
        this.Step = Step;
        this.BeforeDraw = BeforeDraw;
        this.ResizeWidth = ResizeWidth;
        this.ResizeHeight = ResizeHeight;
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
    }

    /// <inheritdoc/>
    protected override void Update(GameTime GameTime)
    {
        if (Frame > 0) Step?.Invoke(Scene, Frame);
        base.Update(GameTime);
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime GameTime)
    {
        if (Frame > 0) BeforeDraw?.Invoke(Scene, Frame);
        base.Draw(GameTime);
        Frame++;

        if (Frame == 10)
        {
            Console.WriteLine($"Window={Window.ClientBounds}, backbuffer={GraphicsDevice.PresentationParameters.BackBufferWidth}x{GraphicsDevice.PresentationParameters.BackBufferHeight}, renderer={Scene.Renderer.ScreenSize}, presentation viewport={GraphicsDevice.Viewport.Bounds}");
            Capture("initial");
        }

        if (Frame == 15)
        {
            GraphicsDeviceManager.PreferredBackBufferWidth = ResizeWidth;
            GraphicsDeviceManager.PreferredBackBufferHeight = ResizeHeight;
            GraphicsDeviceManager.ApplyChanges();
        }

        if (Frame == 90)
        {
            Capture("resized");

            if (Scene.Renderer.ScreenWidth != ResizeWidth || Scene.Renderer.ScreenHeight != ResizeHeight)
                throw new Exception($"Resize failed: {Scene.Renderer.ScreenSize}");

            Console.WriteLine($"PASS: {Scene.GetType().Name}, {Frame} Vulkan frames, {Scene.ECS.EntityCount} entities, resize and content loading.");
            Exit();
        }
    }

    /// <summary>Saves the rendered frame to the configured capture destination.</summary>
    private void Capture(string Suffix)
    {
        var Geometry = Scene.ECS.GetSystem<Geometry>();

        if (Geometry != null)
        {
            var GeometryPixels = new Color[Geometry.EmissiveTexture.Width * Geometry.EmissiveTexture.Height];
            Geometry.EmissiveTexture.GetData(GeometryPixels);
            Console.WriteLine($"Geometry emissive: {Geometry.EmissiveTexture.Width}x{Geometry.EmissiveTexture.Height}, lit={GeometryPixels.Count(Pixel => Pixel.R > 12 || Pixel.G > 12 || Pixel.B > 12)}, entities={Scene.ECS.EntityCount}");
        }

        var Target = Scene.Renderer.SceneRT;
        var Pixels = new Color[Target.Width * Target.Height];
        Target.GetData(Pixels);

        int Lit = Pixels.Count(Pixel => Pixel.R > 12 || Pixel.G > 12 || Pixel.B > 12);
        Directory.CreateDirectory(Output);

        using (var Stream = File.Create(Path.Combine(Output, $"{Scene.GetType().Name}-{Suffix}.png")))
            Target.SaveAsPng(Stream, Target.Width, Target.Height);

        Console.WriteLine($"Frame {Frame}: target={Target.Width}x{Target.Height}, visible pixels={Lit}");

        if (Lit < 500) throw new Exception("Rendered frame is empty or nearly black.");
    }
}
