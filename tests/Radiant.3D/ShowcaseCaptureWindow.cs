using System;
using System.IO;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;

internal sealed class ShowcaseCaptureWindow : Window
{
    private readonly CornellBoxScene Scene = new() { InitialRenderMethod = 2, InitialDenoiser = DenoiserMode.Atrous };
    private int Frame;

    public ShowcaseCaptureWindow() : base(new GameLoop(), new() { Title = "Radiant Cornell Box capture", Width = 1280, Height = 720 })
    {
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(0);
    }

    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        if (++Frame != 128) return;

        Directory.CreateDirectory("artifacts/showcase");
        var target = Scene.Renderer.SceneRT;
        using var output = File.Create("artifacts/showcase/cornell-box.png");
        target.SaveAsPng(output, target.Width, target.Height);
        Console.WriteLine($"CAPTURE: Cornell Box, {target.Width}x{target.Height}, {Scene.Tracer.SampleCount} samples, À-trous denoising.");
        Exit();
    }
}
