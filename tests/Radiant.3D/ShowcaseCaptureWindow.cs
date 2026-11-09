using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.IO;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;

/// <summary>Runs showcase capture checks and records diagnostic frames.</summary>
internal sealed class ShowcaseCaptureWindow : Window
{
    private readonly CornellBoxScene Scene;
    private int Frame;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public ShowcaseCaptureWindow(DenoiserMode Denoiser = DenoiserMode.Atrous) : base(new GameLoop(), new() { Title = "Radiant Cornell Box capture", Width = 1280, Height = 720 })
    {
        Scene = new() { InitialRenderMethod = 2, InitialDenoiser = Denoiser };
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(0);
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);

        if (++Frame != 128) return;

        Directory.CreateDirectory("artifacts/showcase");

        var target = Scene.Renderer.SceneRT;
        using var output = File.Create(Scene.Denoiser.Mode == DenoiserMode.Atrous ? "artifacts/showcase/cornell-box.png" : $"artifacts/showcase/cornell-box-{Scene.Denoiser.BackendId}.png");
        target.SaveAsPng(output, target.Width, target.Height);
        Console.WriteLine($"CAPTURE: Cornell Box, {target.Width}x{target.Height}, {Scene.Tracer.SampleCount} samples, {Scene.Denoiser.BackendId} denoising, {Scene.Tracer.ActiveBackend}, ray dispatch {Scene.Tracer.TraceGpuMilliseconds:F3} ms.");
        Exit();
    }
}
