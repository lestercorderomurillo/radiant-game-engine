using System;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

/// <summary>Connects native tracing signals to NRD for denoising regression checks.</summary>
internal sealed class NrdRegressionScene : Scene
{
    public readonly VulkanRTGeometry Geometry = new();
    public PathTracer Tracer;
    public Denoising Denoiser;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        Geometry.SetPlane();
        Tracer = ECS.AddSystem(new PathTracer { Geometry = Geometry, Channels = TraceChannels.All,
            ResolutionScale = 1, MaxBounces = 2, SeparateLighting = true, Environment = new(0.4f, 0.6f, 0.8f) });
        Denoiser = ECS.AddSystem(new Denoising { TraceSource = Tracer, Input = Tracer.GetOutput,
            Albedo = () => Tracer.AlbedoTexture, Normals = () => Tracer.NormalTexture,
            Depth = () => Tracer.DepthTexture, Emission = () => Tracer.EmissionTexture,
            SingleSample = () => Tracer.SampleTexture, Camera = () => Geometry.HasCamera ? Geometry.Camera : null,
            Revision = () => Tracer.RadianceRevision, After = [Tracer] });

        var tone = ECS.AddSystem(new ToneMapping { Input = Denoiser.GetOutput, After = [Denoiser] });
        ECS.AddSystem(new RenderPresentation { Input = tone.GetOutput, After = [tone] });
        base.SetupECS();
    }
}

/// <summary>Runs nrd regression checks and records diagnostic frames.</summary>
internal sealed class NrdRegressionWindow : Window
{
    private readonly NrdRegressionScene Scene = new();
    private readonly bool ExpectUnavailable;
    private int Frame;
    private Vector4[] ReferenceSum;
    private int ReferenceFrames;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public NrdRegressionWindow(bool ExpectUnavailable) : base(new GameLoop(), new() { Title = "Radiant NRD regression", Width = 320, Height = 200 })
    {
        this.ExpectUnavailable = ExpectUnavailable;
        GameLoop.AddScene(Scene);
        GameLoop.SetTargetFps(0);
        GameLoop.ThrottleUnfocused = false;
    }

    /// <inheritdoc/>
    protected override void Update(GameTime Time)
    {
        base.Update(Time);

        if (Frame == 0)
        {
            bool available = Scene.Denoiser.TrySelectBackend("nrd.reblur", out var reason);

            if (available == ExpectUnavailable) throw new Exception($"Unexpected NRD availability: {reason}");
            if (ExpectUnavailable) { Console.WriteLine($"PASS NRD unavailable: {reason}"); Exit(); return; }
        }

        if (ExpectUnavailable) return;
        if (Frame is 8 or 32 && !Scene.Denoiser.TrySelectBackend("nrd.relax", out var reasonRelax)) throw new Exception(reasonRelax);

        if (Frame == 12) { Scene.Geometry.Emission *= 2; Scene.Geometry.SetPlane(); }

        if (Frame == 16) Scene.Geometry.Size = new(81, 55);
        if (Frame == 20) Scene.Geometry.HasCamera = false;
        if (Frame == 22) Scene.Geometry.HasCamera = true;
        if (Frame == 24) Scene.Tracer.Backend = PathTracingBackend.Software;
        if (Frame == 26) Scene.Tracer.Backend = PathTracingBackend.Auto;
        if (Frame == 30) Scene.Tracer.Channels = TraceChannels.None;
        if (Frame == 32) Scene.Tracer.Channels = TraceChannels.All;
        if (Frame == 28) Scene.Denoiser.Mode = DenoiserMode.None;

        if (Frame == 30 && !Scene.Denoiser.TrySelectBackend("nrd.reblur", out var reasonReblur)) throw new Exception(reasonReblur);

        if (Frame == 34) { Scene.Geometry.Triangles = []; Scene.Geometry.Revision++; }

        if (Frame == 36) { Scene.Geometry.SetPlane(); Scene.Denoiser.ResetHistory(); }

        if (Frame is 42 or 106)
        {
            if (!Scene.Denoiser.TrySelectBackend(Frame == 42 ? "nrd.relax" : "nrd.reblur", out var reason)) throw new Exception(reason);

            Scene.Geometry.SetPlane();

            var triangles = new global::System.Collections.Generic.List<TraceTriangle>(Scene.Geometry.Triangles);
            triangles.Add(new(new(0.2f, -0.5f, 1), new(1.2f, -0.5f, 1), new(1.2f, 0.5f, 1), Vector3.Zero, new(5, 3, 1)));
            triangles.Add(new(new(0.2f, -0.5f, 1), new(1.2f, 0.5f, 1), new(0.2f, 0.5f, 1), Vector3.Zero, new(5, 3, 1)));
            Scene.Geometry.Triangles = triangles;
            Scene.Geometry.Revision++;
            ReferenceSum = null;
            ReferenceFrames = 0;
        }

        if (Frame == 38) Scene.Geometry.Camera = Scene.Geometry.Camera with { Projection = Matrix.CreatePerspectiveFieldOfView(1, 1, 0.1f, 10), Orthographic = false };
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime Time)
    {
        if (ExpectUnavailable) return;

        base.Draw(Time);

        var output = Scene.Denoiser.GetOutput();

        if (output.Width != Scene.Geometry.Size.X || output.Height != Scene.Geometry.Size.Y) throw new Exception("NRD resize failed.");

        var pixels = new Vector4[output.Width * output.Height];
        output.GetData(pixels);

        if (Frame >= 42)
        {
            var raw = new Vector4[pixels.Length];
            Scene.Tracer.SampleTexture.GetData(raw);
            ReferenceSum ??= new Vector4[pixels.Length];
            ReferenceFrames++;

            for (int index = 0; index < pixels.Length; index++) ReferenceSum[index] += raw[index];

            if (Frame is 105 or 169)
            {
                double rawError = 0, filteredError = 0;

                for (int index = 0; index < pixels.Length; index++)
                {
                    var reference = ReferenceSum[index] / ReferenceFrames;
                    rawError += Vector4.DistanceSquared(raw[index], reference);
                    filteredError += Vector4.DistanceSquared(pixels[index], reference);
                }

                if (!double.IsFinite(filteredError) || filteredError >= rawError * 0.9) throw new Exception($"NRD did not reduce noisy diffuse error: raw={rawError}, filtered={filteredError}");

                Console.WriteLine($"PASS {Scene.Denoiser.BackendId} noisy diffuse MSE ratio: {filteredError / rawError:F3}");
            }

            if (++Frame == 170) Exit();

            return;
        }

        var expected = !Scene.Geometry.HasCamera ? Vector3.Zero : Frame is 34 or 35 ? Scene.Tracer.Environment : Scene.Geometry.Emission + Scene.Geometry.Albedo * Scene.Tracer.Environment;
        float maximum = 0;

        for (int index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];

            if (!float.IsFinite(pixel.X) || !float.IsFinite(pixel.Y) || !float.IsFinite(pixel.Z)) throw new Exception("NRD produced nonfinite output.");

            // Ignore shared-edge precision in software fallback and NRD's boundary footprint.
            int x = index % output.Width, y = index / output.Width;

            if (x < 4 || y < 4 || x >= output.Width - 4 || y >= output.Height - 4) continue;
            if (Math.Abs((x + 0.5f) / output.Width + (y + 0.5f) / output.Height - 1) < 0.00001f) continue;

            maximum = Math.Max(maximum, Vector3.Distance(new(pixel.X, pixel.Y, pixel.Z), expected));
        }

        if (maximum > 0.03f) throw new Exception($"NRD analytic error at frame {Frame}: {maximum}, expected {expected}, center {pixels[pixels.Length / 2]}");

        Console.WriteLine($"PASS NRD frame {Frame}: {Scene.Denoiser.BackendId}, error={maximum:G3}");
        Frame++;
    }
}
