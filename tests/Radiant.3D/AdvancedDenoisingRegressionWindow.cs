using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

/// <summary>Runs advanced denoising regression checks and records diagnostic frames.</summary>
internal sealed class AdvancedDenoisingRegressionWindow : Window
{
    private readonly Scene Scene;
    private readonly bool TwoD;
    private readonly Stopwatch Timeout = new();
    private int Frame, NeuralStage, Jobs, CameraEntity;
    private Vector4[] Raw16, Temporal16;
    private bool NeuralReady;
    private PathTracer Tracer => Scene is CornellBoxScene c ? c.Tracer : ((Rendering2DScene)Scene).Tracer;
    private AntiAliasing AA => Scene is CornellBoxScene c ? c.AA : ((Rendering2DScene)Scene).AA;
    private string SettingsId => TwoD ? "rendering2dPath" : "pathTracing";

    /// <summary>Finds a named inspector widget required by the regression case.</summary>
    private Widget Widget(string id, bool neural = false)
    {
        var windows = (Dictionary<string, WindowData>)typeof(Inspector).GetField("Windows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Scene.ECS.GetSystem<Inspector>());
        var window = windows[SettingsId + (neural ? "Neural" : "")];
        return window.Widgets[window.WidgetIndex[id]];
    }

    private Denoising Filter => Scene is CornellBoxScene c ? c.Denoiser : ((Rendering2DScene)Scene).Denoiser;

    /// <summary>Applies a renderer or denoiser selection through the test's inspector controls.</summary>
    private void Select(int method) { if (Scene is CornellBoxScene c) c.SelectRenderer(method); else ((Rendering2DScene)Scene).SelectRenderer(method); }

    /// <summary>Changes the test camera to exercise history invalidation.</summary>
    private void SetCamera(int entity) { if (Scene is CornellBoxScene c) c.Meshes.CameraEntity = entity; else ((Rendering2DScene)Scene).Geometry.CameraEntity = entity; }

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public AdvancedDenoisingRegressionWindow(bool twoD) : base(new GameLoop(), new() { Title = "Radiant temporal and neural denoising regression", Width = 640, Height = 360 })
    {
        TwoD = twoD;
        Scene = twoD ? new Rendering2DScene() : new CornellBoxScene();
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(60);
    }

    /// <inheritdoc/>
    protected override void Update(GameTime time)
    {
        base.Update(time);

        if (Frame == 0)
        {
            Select(2);
            Tracer.ResolutionScale = 0.25f;
            Filter.Mode = DenoiserMode.Svgf;

            var options = Filter.GetOptions();

            for (int i = 0; i < options.Count; i++) if (!options[i].Available)
            {
                Widget("denoiser").DropdownCallback(i);

                if (Filter.Mode != DenoiserMode.Svgf || Widget("denoiser").DropdownSelected != 3 || Filter.TrySelectBackend(options[i].Id, out _)) throw new Exception("Unavailable backend was selected.");
            }

            if (Filter.TrySelectBackend("unknown", out _)) throw new Exception("Unknown backend was selected.");
        }

        if (Frame == 16) Filter.Mode = DenoiserMode.None;

        if (Frame == 17) { Filter.RegisterBackend("test.passthrough", () => new TestBackend()); if (!Filter.TrySelectBackend("test.passthrough", out _)) throw new Exception("Registered backend unavailable."); }

        if (Frame == 18) Filter.Mode = DenoiserMode.None;

        if (Frame == 257 && TwoD) { CameraEntity = Scene.ECS.CreateEntity(); Scene.ECS.AddComponent<Camera2D>(CameraEntity); SetCamera(CameraEntity); }

        if (Frame == 258)
        {
            if (TwoD) Scene.ECS.GetComponent<Camera2D>(CameraEntity).Position.X += 0.01f;
            else Scene.ECS.GetComponent<Camera3D>(((CornellBoxScene)Scene).Meshes.CameraEntity).Position.X += 0.01f;
        }

        if (Frame == 256) Filter.Mode = DenoiserMode.Svgf;
        if (Frame == 259) Tracer.ResetAccumulation();
        if (Frame == 260) Tracer.Environment += new Vector3(0.01f);

        if (Frame == 261) { Filter.Iterations = 1; AA.Mode = PostAAMode.FXAA; }

        if (Frame == 262) { Filter.Iterations = 2; AA.Mode = PostAAMode.SMAA; }

        if (Frame == 263) { Filter.Iterations = 5; AA.Mode = PostAAMode.TAA; }

        if (Frame == 264) SetCamera(int.MaxValue);

        if (Frame == 265) { SetCamera(-1); Tracer.ResolutionScale = 0.5f; }

        if (Frame == 266) Select(0);

        if (Frame == 267) { AA.Mode = PostAAMode.None; Select(2); Tracer.ResolutionScale = 0.25f; }

        if (Frame == 268) Filter.Mode = DenoiserMode.None;

        if (Frame >= 269 && NeuralStage == 3 && Filter.NeuralBusy)
        {
            // Invalidate a queued result before Render can publish it, even when native inference has already finished.
            Filter.ResetHistory();
            Tracer.Environment = new Vector3(0.33f);
            Tracer.ResetAccumulation();
            NeuralStage = 4;
            Timeout.Restart();
        }

        if (Frame == 269)
        {
            if (!Filter.TrySelectBackend("oidn", out var reason)) { Console.WriteLine("SKIP: optional OIDN runtime: " + reason); Exit(); return; }

            Widget("denoiser").DropdownCallback(4);
            Widget("device", true).DropdownCallback((int)OidnDevice.CPU);
            Widget("quality", true).DropdownCallback((int)OidnQuality.Fast);
            Filter.NeuralUpdateInterval = 1;
            Timeout.Restart();
        }
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);

        if (Frame < 16 && Frame > 0)
        {
            if (Filter.TemporalFrames != Frame + 1) throw new Exception("SVGF history did not advance.");

            CheckFinite(Filter.GetOutput());

            var moments = Read(Filter.Buffers["Luminance moments"]());
            var temporal = Read(Filter.Buffers["Temporal illumination"]());

            if (moments.Max(p => p.Z) < Math.Min(Frame, 4) || temporal.Any(p => p.W < 0)) throw new Exception("SVGF moments/history/variance are invalid.");
        }

        if (Frame == 15) { Raw16 = Read(Tracer.GetOutput()); Temporal16 = Read(Filter.GetOutput()); Save("svgf-16spp", Filter.GetOutput()); }

        if (Frame == 16 && Filter.Buffers["Luminance moments"]() != null) throw new Exception("None retained SVGF targets.");
        if (Frame == 17 && (Filter.GetOutput() != Tracer.GetOutput() || Filter.Buffers["Extension sample"]() != Tracer.GetOutput() || TestBackend.Created != 2 || TestBackend.Disposed != 1)) throw new Exception("Backend registration, buffers or ownership failed.");
        if (Frame == 18 && (Filter.Buffers["Extension sample"]() != null || TestBackend.Disposed != 2)) throw new Exception("External backend was not disposed.");

        if (Frame == 258) CheckFinite(Filter.GetOutput());

        if (Frame == 258 && (Filter.TemporalFrames < 2 || Read(Filter.Buffers["Luminance moments"]()).Max(p => p.Z) < 1.5f)) throw new Exception($"Camera reprojection lost valid temporal history: frames={Filter.TemporalFrames}, moments={Read(Filter.Buffers["Luminance moments"]()).Max(p => p.Z)}, camera={Tracer.Geometry.Camera}.");

        if (Frame == 255)
        {
            var reference = Read(Tracer.GetOutput());
            var normals = new Color[reference.Length];
            Tracer.NormalTexture.GetData(normals);

            var albedo = new Color[reference.Length];
            Tracer.AlbedoTexture.GetData(albedo);

            var emitters = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[reference.Length];
            Tracer.EmissionTexture.GetData(emitters);

            double rawError = 0, temporalError = 0;
            int count = 0;

            for (int y = 4; y < 86; y++) for (int x = 4; x < 156; x++)
            {
                int i = y * 160 + x;

                if (normals[i].A == 0) continue;

                bool interior = true;

                for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++) { int j = i + dy * 160 + dx; interior &= normals[j] == normals[i] && albedo[j] == albedo[i] && emitters[j].ToVector4().ToNumerics().X == 0; }

                if (!interior) continue;

                rawError += Vector4.DistanceSquared(Raw16[i], reference[i]);
                temporalError += Vector4.DistanceSquared(Temporal16[i], reference[i]);
                count++;
            }

            Console.WriteLine($"SVGF 16 frames vs 256spp: raw MSE={rawError / count:F6}, temporal MSE={temporalError / count:F6}, pixels={count}");
            Console.WriteLine($"Means: raw={Raw16.Average(p => p.X):F6}, svgf={Temporal16.Average(p => p.X):F6}, ref={reference.Average(p => p.X):F6}");

            if (count < 500 || !TwoD && temporalError >= rawError) throw new Exception("SVGF did not reduce Cornell diffuse noise.");
        }

        if (Frame is 256 or 259 or 260 or 265 or 267) if (Filter.TemporalFrames != 1) throw new Exception($"SVGF retained obsolete history at {Frame}: {Filter.TemporalFrames}.");

        if (Frame is >= 261 and <= 263) { CheckFinite(Filter.GetOutput()); CheckFinite(AA.GetOutput()); }

        if (Frame == 264 && (Filter.TemporalFrames != 0 || Read(Filter.GetOutput()).Any(p => p != Vector4.Zero))) throw new Exception("SVGF retained pixels without a camera.");
        if (Frame == 265 && Filter.GetOutput().Width != 320) throw new Exception("SVGF did not resize.");
        if (Frame == 266 && (Filter.GetOutput() != null || Filter.Buffers["Surface history"]() != null)) throw new Exception("Inactive tracing retained temporal resources.");

        if (Frame == 268) Console.WriteLine("PASS: per-view backend registration/disposal/buffers; SVGF moments, camera reprojection, raw sample isolation, 1/2/5 wavelet passes, reset, environment, missing camera, resize and renderer switching.");
        if (Frame >= 269) CheckNeural();
        Frame++;
    }

    /// <summary>Checks the optional neural denoiser's output and fallback behavior.</summary>
    private void CheckNeural()
    {
        if (Timeout.Elapsed.TotalSeconds > 45) throw new Exception("OIDN timed out: " + Filter.Status);
        if (Filter.Status.Contains("OIDN:") || Filter.Status.Contains("no supported") && NeuralStage != 6) throw new Exception(Filter.Status);
        if (NeuralStage == 5)
        {
            if (Filter.Buffers["Neural output"]() != null) throw new Exception("None retained the neural backend.");

            Exit();
            return;
        }
        if (NeuralStage == 4)
        {
            if (Filter.NeuralJobs <= Jobs) return;

            CheckFinite(Filter.GetOutput());

            var current = Read(Tracer.GetOutput());
            var output = Read(Filter.GetOutput());
            var normals = new Color[output.Length];
            Tracer.NormalTexture.GetData(normals);

            int environmentPixels = 0;

            for (int i = 0; i < output.Length; i++) if (normals[i].A == 0)
            {
                if (output[i] != current[i]) throw new Exception($"OIDN reused an obsolete snapshot at {i}: output={output[i]}, current={current[i]}.");

                // Center-ray guides can miss while stochastic rays cover a nearby edge; only true misses equal the environment.
                if (Vector3.Distance(new Vector3(output[i].X, output[i].Y, output[i].Z), Tracer.Environment) < 0.001f) environmentPixels++;
            }

            if (environmentPixels < 10) throw new Exception("The new environment epoch never reached neural compositing.");

            Jobs = Filter.NeuralJobs;
            Widget("device", true).DropdownCallback((int)OidnDevice.GPU);
            NeuralStage = 6;
            Timeout.Restart();
            Console.WriteLine("PASS: queued native work cancellation/discard, scene epoch reset, current background and reconstruction recovery.");

            return;
        }
        if (NeuralStage == 6)
        {
            bool unsupported = Filter.Status.Contains("no supported GPU");

            if (!unsupported && Filter.NeuralJobs <= Jobs) return;
            if (unsupported && Filter.GetOutput() != Tracer.GetOutput()) throw new Exception("Unavailable GPU inference did not fall back to raw radiance.");

            Console.WriteLine(unsupported ? "PASS: unavailable OIDN GPU reports capability failure and forwards raw radiance." : "PASS: actual OIDN GPU inference: " + Filter.Status);
            Jobs = Filter.NeuralJobs;
            Widget("device", true).DropdownCallback((int)OidnDevice.CPU);
            NeuralStage = 7;
            Timeout.Restart();

            return;
        }
        if (NeuralStage == 7)
        {
            if (Filter.NeuralJobs <= Jobs) return;

            CheckFinite(Filter.GetOutput());
            Filter.Mode = DenoiserMode.None;
            NeuralStage = 5;
            Console.WriteLine("PASS: CPU reconstruction recovers after GPU device selection/failure.");

            return;
        }
        if (NeuralStage == 3) return;

        if (!NeuralReady && Filter.NeuralJobs > Jobs)
        {
            NeuralReady = true;
            CheckFinite(Filter.GetOutput());

            var raw = Read(Tracer.GetOutput());
            var output = Read(Filter.GetOutput());
            var normals = new Color[raw.Length];
            Tracer.NormalTexture.GetData(normals);

            var emission = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[raw.Length];
            Tracer.EmissionTexture.GetData(emission);

            if (output.Where((p, i) => p.W != raw[i].W).Any()) throw new Exception("OIDN changed coverage.");

            for (int i = 0; i < output.Length; i++) if ((raw[i].W == 0 || normals[i].A == 0 || emission[i].ToVector4().ToNumerics().X > 0) && output[i] != raw[i]) throw new Exception("OIDN changed a miss/emitter.");

            if (!TwoD && output.SequenceEqual(raw)) throw new Exception("OIDN returned an unfiltered passthrough.");

            Console.WriteLine("PASS: actual native inference: " + Filter.Status);
            Save("oidn-" + Filter.NeuralQuality, Filter.GetOutput());
            Jobs = Filter.NeuralJobs;
            NeuralStage++;

            if (NeuralStage < 3) { Widget("quality", true).DropdownCallback(NeuralStage); NeuralReady = false; Timeout.Restart(); }
            else
            {
                // Invalidate a pending/completed neural image before it can be reused for another scene epoch.
                Filter.ResetHistory();
                Tracer.ResetAccumulation();

                if (Filter.GetOutput() != Tracer.GetOutput()) throw new Exception("Reset retained obsolete neural output.");

                Timeout.Restart();
                Console.WriteLine("PASS: OIDN Fast/Balanced/High, finite HDR, coverage, direct emitters, stale history rejection and raw fallback.");
            }
        }
    }

    /// <summary>Minimal registered denoiser used to verify backend selection and lifecycle.</summary>
    private sealed class TestBackend : IDenoisingBackend
    {
        public static int Created, Disposed;
        public string Name => "Test passthrough";
        public bool Available => true;
        public string Status => "Test only";

        private Texture2D Input;

        public IReadOnlyDictionary<string, Func<Texture2D>> Buffers { get; }

        /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
        public TestBackend() { Created++; Buffers = new Dictionary<string, Func<Texture2D>> { ["Extension sample"] = GetOutput }; }

        /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
        public Texture2D GetOutput() => Input;

        /// <summary>Records the supplied frame and exposes the test backend's output.</summary>
        public void Render(in DenoisingFrame frame) => Input = frame.Input;

        /// <summary>Discards accumulated image history so the next frame starts fresh.</summary>
        public void ResetHistory() { }

        /// <summary>Records disposal of the test backend and releases its test resources.</summary>
        public void Dispose() { Disposed++; Input = null; }
    }

    /// <summary>Reads a rendered pixel or image for the current regression assertion.</summary>
    private static Vector4[] Read(Texture2D texture)
    {
        if (texture.Format == SurfaceFormat.Vector4) { var data = new Vector4[texture.Width * texture.Height]; texture.GetData(data); return data; }
        if (texture.Format == SurfaceFormat.HalfVector4) { var data = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[texture.Width * texture.Height]; texture.GetData(data); return data.Select(p => p.ToVector4().ToNumerics()).ToArray(); }

        var colors = new Color[texture.Width * texture.Height];
        texture.GetData(colors);

        return colors.Select(p => p.ToVector4()).ToArray();
    }

    /// <summary>Rejects NaN or infinite values in the rendered image.</summary>
    private static void CheckFinite(Texture2D texture) { if (texture == null || Read(texture).Any(p => !float.IsFinite(p.X + p.Y + p.Z + p.W))) throw new Exception("Invalid denoised HDR output."); }

    /// <summary>Writes the current test image to its diagnostic output path.</summary>
    private void Save(string name, Texture2D texture)
    {
        Directory.CreateDirectory("artifacts/denoising");

        using var preview = Scene.Renderer.CreateTexture(texture.Width, texture.Height);
        preview.SetData(Read(texture).Select(p => new Color(p)).ToArray());

        using var stream = File.Create($"artifacts/denoising/{(TwoD ? "2d" : "3d")}-{name}.png");
        preview.SaveAsPng(stream, texture.Width, texture.Height);
    }
}
