using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

/// <summary>Runs denoising regression checks and records diagnostic frames.</summary>
internal sealed class DenoisingRegressionWindow : Window
{
    private readonly Scene Scene;
    private readonly bool TwoD;
    private int Frame;
    private Vector4[] Noisy, Bilateral, Atrous, SyntheticPixels;
    private Texture2D TestInput, TestAlbedo, TestNormals, TestDepth, TestEmission, Snapshot;
    private readonly List<(string Edge, DenoiserMode Mode, int Passes)> Synthetic = new();
    private int PathFrame => Frame - Synthetic.Count;
    private PathTracer Tracer => Scene is CornellBoxScene c ? c.Tracer : ((Rendering2DScene)Scene).Tracer;
    private AntiAliasing AA => Scene is CornellBoxScene c ? c.AA : ((Rendering2DScene)Scene).AA;
    private Denoising Denoiser => Scene is CornellBoxScene c ? c.Denoiser : ((Rendering2DScene)Scene).Denoiser;

    /// <summary>Applies a renderer or denoiser selection through the test's inspector controls.</summary>
    private void Select(int method) { if (Scene is CornellBoxScene c) c.SelectRenderer(method); else ((Rendering2DScene)Scene).SelectRenderer(method); }

    /// <summary>Finds a named denoising-settings widget for scripted interaction.</summary>
    private Widget DenoiserWidget()
    {
        var windows = (Dictionary<string, WindowData>)typeof(Inspector).GetField("Windows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Scene.ECS.GetSystem<Inspector>());
        var window = windows[TwoD ? "rendering2dPath" : "pathTracing"];
        return window.Widgets[window.WidgetIndex["denoiser"]];
    }

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public DenoisingRegressionWindow(bool twoD) : base(new GameLoop(), new() { ShowLoader = false, Title = "Radiant denoising regression", Width = 640, Height = 360 })
    {
        TwoD = twoD;
        Scene = twoD ? new Rendering2DScene() : new CornellBoxScene();

        foreach (string edge in new[] { "normal", "depth", "albedo", "alpha", "emission", "constant" })
        {
            Synthetic.Add((edge, DenoiserMode.Bilateral, 1));
            Synthetic.Add((edge, DenoiserMode.Atrous, 2));
            Synthetic.Add((edge, DenoiserMode.Atrous, 3));
        }

        Synthetic.Add(("constant", DenoiserMode.None, 1));
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(0);
    }

    /// <inheritdoc/>
    protected override void Update(GameTime time)
    {
        base.Update(time);

        if (Frame == 0) { Tracer.ResolutionScale = 0.25f; Select(2); CreateSynthetic(); }

        if (Frame < Synthetic.Count) { ConfigureSynthetic(Synthetic[Frame]); return; }

        if (PathFrame == 0) { RestorePathInput(); Denoiser.Mode = DenoiserMode.None; Denoiser.Iterations = 3; Tracer.ResetAccumulation(); }

        if (PathFrame == 4) { Denoiser.Input = () => Snapshot; DenoiserWidget().DropdownCallback((int)DenoiserMode.Bilateral); }

        if (PathFrame == 5) DenoiserWidget().DropdownCallback((int)DenoiserMode.Atrous);

        if (PathFrame == 6) { RestorePathInput(); DenoiserWidget().DropdownCallback((int)DenoiserMode.None); }

        if (PathFrame == 8) { Denoiser.Mode = DenoiserMode.Bilateral; AA.Mode = PostAAMode.FXAA; }

        if (PathFrame == 12) { Denoiser.Mode = DenoiserMode.Atrous; AA.Mode = PostAAMode.SMAA; }

        if (PathFrame == 16) AA.Mode = PostAAMode.TAA;

        if (PathFrame == 20) { Denoiser.Mode = DenoiserMode.None; AA.Mode = PostAAMode.None; }

        if (PathFrame == 256)
        {
            DenoiserWidget().DropdownCallback((int)DenoiserMode.Atrous);
            if (Scene is CornellBoxScene c) c.Meshes.CameraEntity = int.MaxValue; else ((Rendering2DScene)Scene).Geometry.CameraEntity = int.MaxValue;
        }

        if (PathFrame == 257)
        {
            if (Scene is CornellBoxScene c) c.Meshes.CameraEntity = -1; else ((Rendering2DScene)Scene).Geometry.CameraEntity = -1;
            Tracer.ResolutionScale = 0.5f;

            var inspector = Scene.ECS.GetSystem<Inspector>();
            var windows = (Dictionary<string, WindowData>)typeof(Inspector).GetField("Windows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(inspector);

            foreach (string id in windows.Keys.ToArray()) Inspector.HideWindow(id);

            Inspector.ShowWindow(TwoD ? "rendering2dPath" : "pathTracing");
            typeof(Inspector).GetField("GlobalVisible", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(inspector, true);
        }

        if (PathFrame == 258) Select(0);
        if (PathFrame == 259) Select(2);
        if (PathFrame == 260) DenoiserWidget().DropdownCallback((int)DenoiserMode.None);
    }

    /// <summary>Reconnects the path tracer after a synthetic denoising test.</summary>
    private void RestorePathInput()
    {
        Denoiser.Input = () => Tracer.Enabled ? Tracer.GetOutput() : null;
        Denoiser.Albedo = () => Tracer.AlbedoTexture;
        Denoiser.Normals = () => Tracer.NormalTexture;
        Denoiser.Depth = () => Tracer.DepthTexture;
        Denoiser.Emission = () => Tracer.EmissionTexture;
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);

        if (Frame < Synthetic.Count) { CheckSynthetic(Synthetic[Frame]); Frame++; return; }

        Frame++;

        if (PathFrame == 4)
        {
            Noisy = Read(Tracer.GetOutput());
            Save("raw-4spp", Noisy, 160, 90);
            Snapshot = Scene.Renderer.CreateTexture(160, 90, SurfaceFormat.Vector4);
            Snapshot.SetData(Noisy);
        }

        if (PathFrame == 5)
        {
            Bilateral = Read(Denoiser.GetOutput());
            Save("bilateral-4spp", Bilateral, 160, 90);

            if (Denoiser.Buffers["Intermediate"]() != null) throw new Exception("Bilateral allocated an unnecessary ping-pong buffer.");
        }

        if (PathFrame == 6) { Atrous = Read(Denoiser.GetOutput()); Save("atrous-4spp", Atrous, 160, 90); }

        if (PathFrame == 7)
        {
            if (!Noisy.SequenceEqual(Read(Snapshot)) || Tracer.SampleCount != 7) throw new Exception("Denoising mutated its raw input or reset progressive sampling.");
            if (Denoiser.GetOutput() != Tracer.GetOutput() || Denoiser.Buffers["Intermediate"]() != null) throw new Exception("None did not bypass and release denoising targets.");
        }

        if (PathFrame is 12 or 16 or 20)
        {
            var image = AA.GetOutput();

            if (image.Width != 640 || image.Height != 360 || Read(image).Any(p => !float.IsFinite(p.X + p.Y + p.Z + p.W))) throw new Exception("Denoising and post AA produced invalid output.");
            if (AA.Mode == PostAAMode.TAA && AA.HistoryFrames < 2) throw new Exception("Denoised TAA history did not accumulate.");

            Console.WriteLine($"PASS: {Denoiser.Mode} + {AA.Mode} preserves HDR output and valid AA history.");
        }

        if (PathFrame == 256) CompareReference();

        if (PathFrame == 257 && (Tracer.SampleCount != 0 || Read(Denoiser.GetOutput()).Any(p => p != Vector4.Zero))) throw new Exception("Denoising retained stale pixels without a camera.");
        if (PathFrame == 258 && (Denoiser.GetOutput().Width != 320 || Denoiser.GetOutput().Height != 180)) throw new Exception("Denoising did not follow tracing resolution changes.");

        if (PathFrame == 258)
        {
            using var file = File.Create($"artifacts/denoising/{(TwoD ? "2d" : "3d")}-settings.png");
            var target = Scene.Renderer.SceneRT;
            target.SaveAsPng(file, target.Width, target.Height);
        }

        if (PathFrame == 259 && (Denoiser.GetOutput() != null || Denoiser.Buffers["Intermediate"]() != null)) throw new Exception("Inactive tracing retained denoising targets.");
        if (PathFrame == 260 && (Denoiser.GetOutput() == null || Tracer.SampleCount != 1)) throw new Exception("Denoising did not resume with the tracer.");

        if (PathFrame == 261)
        {
            if (Denoiser.GetOutput() != Tracer.GetOutput() || Denoiser.Buffers["Intermediate"]() != null) throw new Exception("Denoising selector did not restore raw output.");

            Snapshot.Dispose();
            TestInput.Dispose();
            TestAlbedo.Dispose();
            TestNormals.Dispose();
            TestDepth.Dispose();
            TestEmission.Dispose();
            Console.WriteLine("PASS: denoiser selector, raw input isolation, progressive samples, missing camera, resolution, renderer switching and None cleanup.");
            Exit();
        }
    }

    /// <summary>Compares denoised pixels with the reference image for the current case.</summary>
    private void CompareReference()
    {
        var reference = Read(Tracer.GetOutput());
        var normals = new Color[reference.Length];
        Tracer.NormalTexture.GetData(normals);

        double rawError = 0, bilateralError = 0, atrousError = 0;
        int count = 0;

        for (int y = 3; y < 87; y++) for (int x = 3; x < 157; x++)
        {
            int i = y * 160 + x;

            if (normals[i].A == 0) continue;

            bool interior = true;

            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) interior &= normals[i + dy * 160 + dx] == normals[i];

            if (!interior) continue;

            rawError += Vector4.DistanceSquared(Noisy[i], reference[i]);
            bilateralError += Vector4.DistanceSquared(Bilateral[i], reference[i]);
            atrousError += Vector4.DistanceSquared(Atrous[i], reference[i]);
            count++;
        }

        if (count < 500) throw new Exception("Denoising reference comparison did not cover enough surfaces.");

        Console.WriteLine($"{(TwoD ? "2d" : "3d")} 4spp versus 256spp interior MSE: raw={rawError / count:F6}, bilateral={bilateralError / count:F6}, atrous={atrousError / count:F6}, pixels={count}");

        if (!TwoD && (bilateralError >= rawError || atrousError >= rawError)) throw new Exception("Denoisers did not reduce Cornell path tracing error.");

        Save("reference-256spp", reference, 160, 90);
    }

    /// <summary>Creates controlled radiance and guide images for denoising checks.</summary>
    private void CreateSynthetic()
    {
        TestInput = Scene.Renderer.CreateTexture(64, 32, SurfaceFormat.Vector4);
        TestAlbedo = Scene.Renderer.CreateTexture(64, 32);
        TestNormals = Scene.Renderer.CreateTexture(64, 32);
        TestDepth = Scene.Renderer.CreateTexture(64, 32, SurfaceFormat.Single);
        TestEmission = Scene.Renderer.CreateTexture(64, 32, SurfaceFormat.Vector4);
        Denoiser.Input = () => TestInput;
        Denoiser.Albedo = () => TestAlbedo;
        Denoiser.Normals = () => TestNormals;
        Denoiser.Depth = () => TestDepth;
        Denoiser.Emission = () => TestEmission;
    }

    /// <summary>Selects synthetic input and filter settings for the next denoising case.</summary>
    private void ConfigureSynthetic((string Edge, DenoiserMode Mode, int Passes) test)
    {
        SyntheticPixels = new Vector4[64 * 32];

        var colors = new Color[SyntheticPixels.Length];
        var normals = new Color[SyntheticPixels.Length];
        var depths = new float[SyntheticPixels.Length];
        var emission = new Vector4[SyntheticPixels.Length];

        for (int y = 0; y < 32; y++) for (int x = 0; x < 64; x++)
        {
            int i = y * 64 + x;
            bool right = x >= 32;
            float value = right ? test.Edge == "emission" ? 16 : 3 : 1;
            float coverage = test.Edge == "alpha" && right ? 0.75f : 0.5f;
            float noise = test.Edge == "emission" && right ? 0 : (x + y) % 2 == 0 ? -0.5f : 0.5f;
            SyntheticPixels[i] = test.Edge == "constant" ? new Vector4(2, 3, 4, 0.5f) : new Vector4(new Vector3((value + noise) * coverage), coverage);
            colors[i] = test.Edge == "albedo" && right ? new Color(0.25f, 0.25f, 0.25f, 1) : Color.White;
            normals[i] = test.Edge == "normal" && right ? new Color(1f, 0.5f, 0.5f, 1) : new Color(0.5f, 0.5f, 1f, 1);
            depths[i] = test.Edge == "depth" && right ? 0.8f : 0.3f;
            emission[i] = test.Edge == "emission" && right ? new Vector4(16, 16, 16, 1) : Vector4.Zero;
        }

        TestInput.SetData(SyntheticPixels);
        TestAlbedo.SetData(colors);
        TestNormals.SetData(normals);
        TestDepth.SetData(depths);
        TestEmission.SetData(emission);
        Denoiser.Mode = test.Mode;
        Denoiser.Iterations = test.Passes;
    }

    /// <summary>Checks the filter's response to the configured synthetic input.</summary>
    private void CheckSynthetic((string Edge, DenoiserMode Mode, int Passes) test)
    {
        var filtered = Read(Denoiser.GetOutput());

        if (test.Mode == DenoiserMode.None)
        {
            if (Denoiser.GetOutput() != TestInput || Denoiser.Buffers["Intermediate"]() != null) throw new Exception("None retained filter resources.");

            return;
        }

        double error = 0;

        for (int y = 0; y < 32; y++) for (int x = 0; x < 64; x++)
        {
            int i = y * 64 + x;
            bool right = x >= 32;
            var pixel = filtered[i];

            if (!float.IsFinite(pixel.X + pixel.Y + pixel.Z + pixel.W) || pixel.W != SyntheticPixels[i].W) throw new Exception($"{test.Mode} {test.Edge} changed alpha/non-finite at {x},{y}: {pixel}.");
            if (test.Edge == "constant") { if (Vector4.Distance(pixel, SyntheticPixels[i]) > 0.0001f) throw new Exception("Denoising changed a constant HDR field."); continue; }

            float expected = right ? test.Edge == "emission" ? 16 : 3 : 1;

            if (Math.Abs(pixel.X / pixel.W - expected) > 0.501f) throw new Exception($"{test.Mode} leaked across {test.Edge} boundaries.");
            if (test.Edge == "emission" && right && pixel != SyntheticPixels[i]) throw new Exception("Denoising modified direct emission.");

            error += Math.Pow(pixel.X / pixel.W - expected, 2);
        }

        if (error / filtered.Length > 0.08) throw new Exception($"{test.Mode} did not remove known checkerboard noise.");

        Console.WriteLine($"PASS: {test.Mode} ({test.Passes} passes), HDR {test.Edge} edges/alpha preserved, noise MSE={error / filtered.Length:F6} (input 0.25).");
    }

    /// <summary>Reads a rendered pixel or image for the current regression assertion.</summary>
    private static Vector4[] Read(Texture2D texture)
    {
        if (texture.Format == SurfaceFormat.Vector4) { var data = new Vector4[texture.Width * texture.Height]; texture.GetData(data); return data; }
        if (texture.Format == SurfaceFormat.HalfVector4) { var data = new HalfVector4[texture.Width * texture.Height]; texture.GetData(data); return data.Select(p => p.ToVector4().ToNumerics()).ToArray(); }

        var colors = new Color[texture.Width * texture.Height];
        texture.GetData(colors);

        return colors.Select(p => p.ToVector4()).ToArray();
    }

    /// <summary>Writes the current test image to its diagnostic output path.</summary>
    private void Save(string name, Vector4[] pixels, int width, int height)
    {
        Directory.CreateDirectory("artifacts/denoising");

        using var preview = Scene.Renderer.CreateTexture(width, height);
        preview.SetData(pixels.Select(p => new Color(p)).ToArray());

        using var stream = File.Create($"artifacts/denoising/{(TwoD ? "2d" : "3d")}-{name}.png");
        preview.SaveAsPng(stream, width, height);
    }
}
