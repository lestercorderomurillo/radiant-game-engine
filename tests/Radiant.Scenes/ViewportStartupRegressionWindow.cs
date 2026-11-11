using System;
using System.IO;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using NativeColor = Microsoft.Xna.Framework.Color;
using Window = com.radiant.engine.backends.monogame.MonoGameWindow;

/// <summary>Checks the actual backbuffer on startup, before any manual resize can repair it.</summary>
internal sealed class ViewportStartupRegressionWindow : Window
{
    private readonly ProbeScene Scene = new();
    private readonly string Mode;
    private int Frames;
    private readonly bool Resize;
    private readonly int CaptureFrame;

    public ViewportStartupRegressionWindow(bool loader, bool maximized, bool resize = false, bool settled = false)
        : base(new GameLoop(), new WindowOptions
        {
            Width = 1280, Height = 720, ShowLoader = loader, Maximized = maximized,
            Title = "Radiant viewport startup regression"
        })
    {
        Mode = $"{(loader ? "loader" : "hidden")}-{(maximized ? "maximized" : "windowed")}";
        Resize = resize;
        CaptureFrame = resize ? 4 : settled ? 5 : 1;
        if (settled) Mode += "-settled";
        if (resize) Mode += "-resized";
        GameLoop.ThrottleUnfocused = false;
        GameLoop.AddScene(Scene);
    }

    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        if (IsLoading) return;
        Frames++;
        var renderer = Scene.Renderer;
        var pp = GraphicsDevice.PresentationParameters;
        if (GetScreenSize() != renderer.ScreenSize || GetScreenCenter() != renderer.ScreenSize / 2)
            throw new Exception("Window metrics disagree with the active presentation surface.");
        if (Resize && Frames == 2)
        {
            GraphicsDeviceManager.PreferredBackBufferWidth = 960;
            GraphicsDeviceManager.PreferredBackBufferHeight = 640;
            GraphicsDeviceManager.ApplyChanges();
            return;
        }
        if (renderer.ScreenWidth != pp.BackBufferWidth || renderer.ScreenHeight != pp.BackBufferHeight)
            throw new Exception("Renderer dimensions are stale after startup.");
        if (Frames == CaptureFrame)
        {
            var pixels = new NativeColor[pp.BackBufferWidth * pp.BackBufferHeight];
            GraphicsDevice.GetBackBufferData(pixels);
            Directory.CreateDirectory("artifacts/viewport-startup");
            using var texture = new Microsoft.Xna.Framework.Graphics.Texture2D(GraphicsDevice, pp.BackBufferWidth, pp.BackBufferHeight);
            texture.SetData(pixels);
            using var capture = File.Create($"artifacts/viewport-startup/{Mode}-{Frames}.png");
            texture.SaveAsPng(capture, texture.Width, texture.Height);
            capture.Flush();
            foreach (var (x, y, expected) in ProbeScene.Markers(pp.BackBufferWidth, pp.BackBufferHeight))
            {
                var actual = pixels[y * pp.BackBufferWidth + x];
                if (actual.R != expected.R || actual.G != expected.G || actual.B != expected.B)
                    throw new Exception($"Startup image is cropped: {Mode} frame {Frames}, marker ({x},{y}) is {actual}, expected {expected}.");
            }
            Console.WriteLine($"PASS: {Mode}, all four backbuffer corners at {pp.BackBufferWidth}x{pp.BackBufferHeight}, frame {Frames}.");
            // DesktopVK backbuffer readback consumes the acquisition semaphore without
            // re-signalling it. Each capture runs in its own process: never present again
            // after readback. StartupRegressionWindow separately covers normal cleanup.
            Environment.Exit(0);
        }
    }

    private sealed class ProbeScene : Scene
    {
        public static (int X, int Y, Color Color)[] Markers(int width, int height) =>
        [
            (width / 20, height / 20, Color.Red),
            (width * 19 / 20, height / 20, new Color(0, 255, 0)),
            (width / 20, height * 19 / 20, new Color(0, 0, 255)),
            (width * 19 / 20, height * 19 / 20, Color.White)
        ];

        public override void SetupScene() => Renderer.FitWorld(Renderer.VirtualSize / 2, Renderer.VirtualSize);

        public override void LateRender()
        {
            var renderer = Renderer;
            renderer.Reset().SetTarget(null).Configure(BlendState.Opaque);
            foreach (var (x, y, color) in Markers(renderer.ScreenWidth, renderer.ScreenHeight))
                renderer.DrawTexture(renderer.GetSolidTexture(color), new Rectangle(x - 8, y - 8, 17, 17));
            renderer.Commit();
        }
    }
}
