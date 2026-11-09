using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.Collections.Generic;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

/// <summary>Analytic plane geometry shared by native and software tracing checks.</summary>
internal sealed class VulkanRTGeometry : ITraceGeometry
{
    public IReadOnlyList<TraceTriangle> Triangles { get; set; } = [];
    public long Revision { get; set; }
    public TraceCamera Camera { get; set; } = new(Matrix.CreateLookAt(new(0, 0, 3), Vector3.Zero, Vector3.UnitY),
        Matrix.CreateOrthographic(2, 2, 0.1f, 10), new(0, 0, 3), true, 10);
    public bool HasCamera { get; set; } = true;
    public Point Size { get; set; } = new(65, 49); // Exercises partial compute workgroups.
    public Vector3 Albedo = new(0.5f, 0.25f, 0.75f);
    public Vector3 Emission = new(0.1f, 0.2f, 0.3f);

    /// <summary>Rebuilds the analytic tracing plane with the requested surface parameters.</summary>
    public void SetPlane(float Depth = 0)
    {
        Triangles = [new(new(-20, -20, Depth), new(20, -20, Depth), new(20, 20, Depth), Albedo, Emission),
            new(new(-20, -20, Depth), new(20, 20, Depth), new(-20, 20, Depth), Albedo, Emission)];
        Revision++;
    }
}

/// <summary>Connects analytic geometry to selectable software and native tracing backends.</summary>
internal sealed class VulkanRTScene : Scene
{
    public readonly VulkanRTGeometry Geometry = new();
    public PathTracer Software, Candidate;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        Geometry.SetPlane();
        Software = ECS.AddSystem(new PathTracer { Backend = PathTracingBackend.Software, Geometry = Geometry,
            Channels = TraceChannels.All, MaxBounces = 2, ResolutionScale = 1, SeparateLighting = true, Environment = new(0.4f, 0.6f, 0.8f) });
        Candidate = ECS.AddSystem(new PathTracer { Backend = PathTracingBackend.Auto, Geometry = Geometry,
            Channels = TraceChannels.All, MaxBounces = 2, ResolutionScale = 1, SeparateLighting = true, Environment = Software.Environment, After = [Software] });

        var tone = ECS.AddSystem(new ToneMapping { Input = Candidate.GetOutput, After = [Candidate] });
        ECS.AddSystem(new RenderPresentation { Input = tone.GetOutput, After = [tone] });
        base.SetupECS();
    }
}

/// <summary>Runs vulkan rtregression checks and records diagnostic frames.</summary>
internal sealed class VulkanRTRegressionWindow : Window
{
    private readonly VulkanRTScene Scene = new();
    private readonly bool ExpectSoftware;
    private int Frame;
    private int PreviousBuilds;
    private bool ExpectEnvironment;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public VulkanRTRegressionWindow(bool ExpectSoftware) : base(new GameLoop(), new() { Title = "Radiant Vulkan RT regression", Width = 320, Height = 200 })
    {
        this.ExpectSoftware = ExpectSoftware;
        GameLoop.AddScene(Scene);
        GameLoop.SetTargetFps(0);
        GameLoop.ThrottleUnfocused = false;
    }

    /// <inheritdoc/>
    protected override void Update(GameTime Time)
    {
        base.Update(Time);

        switch (Frame)
        {
            case 3: Scene.Geometry.Emission *= 2;
            Scene.Geometry.SetPlane();
            break;
            case 5:
                Scene.Geometry.Camera = Scene.Geometry.Camera with { Projection = Matrix.CreatePerspectiveFieldOfView(1, 1, 0.1f, 10), Orthographic = false };
                break;
            case 7: Scene.Geometry.HasCamera = false;
            break;
            case 9: Scene.Geometry.HasCamera = true;
            break;
            case 11:
                Scene.Geometry.Size = new(81, 55);
                GraphicsDeviceManager.PreferredBackBufferWidth = 400;
                GraphicsDeviceManager.PreferredBackBufferHeight = 240;
                GraphicsDeviceManager.ApplyChanges();
                break;
            case 13: Scene.Candidate.Backend = PathTracingBackend.Software;
            break;
            case 15: Scene.Candidate.Backend = ExpectSoftware ? PathTracingBackend.Auto : PathTracingBackend.Vulkan;
            break;
            case 17: Scene.Geometry.Triangles = [];
            Scene.Geometry.Revision++;
            ExpectEnvironment = true;
            break;
            case 19: Scene.Geometry.SetPlane(-20);
            break; // Beyond the far plane.
            case 21: Scene.Geometry.SetPlane(2.95f);
            break; // Before the near plane.
            case 23: Scene.Geometry.SetPlane();
            ExpectEnvironment = false;
            break;
            case 25:
                Scene.Candidate.Channels = TraceChannels.None;
                Scene.Candidate.ResolutionScale = Scene.Software.ResolutionScale = 0.5f;
                break;
            case 27: Scene.Candidate.Channels = TraceChannels.All;
            break;
            case 29:
                Scene.Candidate.Dispose();
                Scene.Software.Dispose();
                break; // Recreate after explicit release.
            case 31:
                Scene.Candidate.Environment = Scene.Software.Environment = new(0.7f, 0.2f, 0.3f);
                Scene.Candidate.ResetAccumulation();
                Scene.Software.ResetAccumulation();
                break;
        }
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime Time)
    {
        base.Draw(Time);
        Frame++;

        var candidate = Scene.Candidate;
        var expectedBackend = ExpectSoftware || Frame is 14 or 15 ? PathTracingBackend.Software : PathTracingBackend.Vulkan;

        if (candidate.ActiveBackend != expectedBackend)
            throw new Exception($"Expected {expectedBackend}, got {candidate.ActiveBackend}: {candidate.BackendStatus}");

        var expected = Scene.Geometry.HasCamera ? ExpectEnvironment ? candidate.Environment : Scene.Geometry.Emission + Scene.Geometry.Albedo * candidate.Environment : Vector3.Zero;
        var actual = Read(candidate.GetOutput());
        var software = Read(Scene.Software.GetOutput());

        if (actual.Length != software.Length) throw new Exception("Backend output dimensions differ.");

        float maxError = 0;

        for (int index = 0; index < actual.Length; index++)
        {
            if (!float.IsFinite(actual[index].X) || !float.IsFinite(actual[index].Y) || !float.IsFinite(actual[index].Z)) throw new Exception("Nonfinite Vulkan radiance.");

            // The software triangle test can miss the shared diagonal through roundoff;
            // hardware triangle traversal is watertight. Both still face the analytic check.
            int width = candidate.GetOutput().Width, height = candidate.GetOutput().Height;
            bool sharedEdge = Math.Abs((index % width + 0.5f) / width + (index / width + 0.5f) / height - 1) < 0.000001f;
            if (!sharedEdge || candidate.ActiveBackend == PathTracingBackend.Vulkan)
                maxError = Math.Max(maxError, Vector3.Distance(new(actual[index].X, actual[index].Y, actual[index].Z), expected));

            if (!sharedEdge && Vector4.Distance(actual[index], software[index]) > 0.002f) throw new Exception($"Hardware/software mismatch at frame {Frame}, pixel {index}: {actual[index]} / {software[index]}");
            if (actual[index].W != (Scene.Geometry.HasCamera ? 1 : 0)) throw new Exception("Incorrect camera/alpha handling.");
        }

        if (maxError > 0.002f) throw new Exception($"Analytic diffuse radiance differs at frame {Frame}: max error {maxError}, expected {expected}, actual {actual[0]}");

        if (candidate.SampleTexture != null)
        {
            var sample = Read(candidate.SampleTexture);

            if (Vector4.Distance(sample[0], actual[0]) > 0.002f) throw new Exception("Shared single-sample texture does not match analytic radiance.");
        }
        else if ((candidate.Channels & TraceChannels.Sample) != 0) throw new Exception("Requested sample buffer is missing.");

        if (Frame == 2) PreviousBuilds = candidate.HardwareBuildCount;

        if (Frame == 3 && candidate.HardwareBuildCount != PreviousBuilds) throw new Exception("Static geometry rebuilt its acceleration structures.");
        if (Frame == 4 && !ExpectSoftware && candidate.HardwareBuildCount != PreviousBuilds + 1) throw new Exception("Geometry revision did not rebuild acceleration structures.");

        if (Frame is 1 or 4 or 6 or 10 or 12 or 14 or 16 or 18 or 20 or 22 or 24 or 26 or 30 or 32)
            if (candidate.SampleCount != 1) throw new Exception($"Accumulation did not reset at frame {Frame}: {candidate.SampleCount}");

        if (Frame is 8 or 9 && candidate.SampleCount != 0) throw new Exception("Missing camera retained accumulation.");

        if (Frame is 1 or 4 or 6 or 8 or 10 or 12 or 14 or 16 or 18 or 20 or 22 or 24 or 26 or 28 or 30 or 32)
            Console.WriteLine($"PASS RT frame {Frame}: {candidate.ActiveBackend}, {candidate.GetOutput().Width}x{candidate.GetOutput().Height}, max analytic error={maxError:G3}, dispatch={candidate.TraceGpuMilliseconds:F3} ms");

        if (Frame != 34) return;
        if (!ExpectSoftware && candidate.TraceGpuMilliseconds < 0) throw new Exception("No GPU timestamp result was collected.");

        if (ExpectSoftware)
        {
            bool rejected = false;
            var previousOutput = candidate.GetOutput();
            var previousSample = candidate.SampleTexture;
            int previousSamples = candidate.SampleCount;
            candidate.Backend = PathTracingBackend.Vulkan;

            try { candidate.Render(); } catch (NotSupportedException) { rejected = true; }

            if (!rejected) throw new Exception("Explicit Vulkan selection silently fell back to software.");
            if (!ReferenceEquals(previousOutput, candidate.GetOutput()) || !ReferenceEquals(previousSample, candidate.SampleTexture) || candidate.SampleCount != previousSamples)
                throw new Exception("Unavailable backend selection destroyed the active tracer state.");
        }

        Console.WriteLine("PASS: backend selection, analytic diffuse transport, shared HDR texture, accumulation, revisions, perspective/orthographic cameras, clipping, empty scene, missing camera, resizing, channel toggles and recreation.");
        Exit();
    }

    /// <summary>Reads a rendered pixel or image for the current regression assertion.</summary>
    private static Vector4[] Read(Texture2D Texture)
    {
        var pixels = new Vector4[Texture.Width * Texture.Height];
        Texture.GetData(pixels);
        return pixels;
    }
}
