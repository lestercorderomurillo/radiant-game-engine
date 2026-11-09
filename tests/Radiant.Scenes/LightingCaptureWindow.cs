using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Microsoft.Xna.Framework.Input;

namespace Radiant.Testing;

/// <summary>Stable scene, camera, cursor and frame count for comparisons between engine builds.</summary>
sealed class FrozenMegaLightsScene : MegaLightsScene
{
    /// <inheritdoc/>
    public override void Update()
    {
        DeltaTime = 0;
        Mouse.SetPosition(80, 80);
        base.Update();
    }
}

/// <summary>Runs lighting capture checks and records diagnostic frames.</summary>
sealed class LightingCaptureWindow : Window
{
    private readonly Scene Scene;
    private readonly string Output;
    private int Frame;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public LightingCaptureWindow(Scene scene, string output)
        : base(new GameLoop(), new() { ShowLoader = false, Title = "HRC comparison", Width = 1024, Height = 640 })
    {
        Scene = scene;
        Output = output;
        Directory.CreateDirectory(output);
        GameLoop.AddScene(scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(0);
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);

        if (++Frame == 30)
        {
            Capture(Scene is MegaLightsScene ? "megalights" : "reference");
            if (Scene is LightingReferenceScene reference) reference.SetBlocker(0);
            else Exit();
        }

        if (Frame == 40 && Scene is LightingReferenceScene glass)
        {
            Capture("opaque");
            glass.SetBlocker(4);
        }

        if (Frame == 50)
        {
            Capture("glass");
            Exit();
        }
    }

    /// <summary>Saves the rendered frame to the configured capture destination.</summary>
    private void Capture(string name)
    {
        var lighting = Scene.ECS.GetSystem<HRCGI>();
        var target = lighting.GetOutput();
        var pixels = new HalfVector4[target.Width * target.Height];
        target.GetData(pixels);

        using (var file = File.Create(Path.Combine(Output, name + ".rgb32f")))
        using (var writer = new BinaryWriter(file))
            foreach (var pixel in pixels)
            {
                var value = pixel.ToVector4().ToNumerics();
                writer.Write(value.X);
                writer.Write(value.Y);
                writer.Write(value.Z);
            }

        var sceneTarget = Scene.Renderer.SceneRT;

        using (var file = File.Create(Path.Combine(Output, name + ".png")))
            sceneTarget.SaveAsPng(file, sceneTarget.Width, sceneTarget.Height);

        // Count actual HRC allocations, excluding geometry, upscalers and driver overhead.
        var targets = new HashSet<RenderTarget2D>();

        foreach (var field in typeof(HRCGI).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (field.GetValue(lighting) is RenderTarget2D single) targets.Add(single);
            if (field.GetValue(lighting) is RenderTarget2D[] array)
                foreach (var item in array) targets.Add(item);
        }

        if (targets.Any(t => t.Format != SurfaceFormat.HalfVector4))
            throw new Exception("Update the allocation counter for the new HRC texture format.");

        var metadata = new
        {
            Width = target.Width, Height = target.Height,
            HrcTargetCount = targets.Count,
            HrcTextureBytes = targets.Sum(t => (long)t.Width * t.Height * 8),
            Frame, RenderScale = Scene.Renderer.RenderScale
        };
        File.WriteAllText(Path.Combine(Output, name + ".json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"CAPTURE {name}: {JsonSerializer.Serialize(metadata)}");
    }
}
