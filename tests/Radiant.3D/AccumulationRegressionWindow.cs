using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.Collections.Generic;
using System.Linq;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

/// <summary>Each pixel sees a strip covering 1% of its area, emitting radiance 6. Its expected mean is 0.06. Rare bright samples expose rounding bias in long-lived history.</summary>
internal sealed class AccumulationGeometry : ITraceGeometry
{
    public IReadOnlyList<TraceTriangle> Triangles { get; }
    public long Revision => 0;
    public TraceCamera Camera => new(Matrix.CreateLookAt(new Vector3(0, 0, 2), Vector3.Zero, Vector3.UnitY), Matrix.CreateOrthographic(16, 16, 0.1f, 10), new Vector3(0, 0, 2), true, 10);
    public bool HasCamera => true;
    public Point Size => new(16, 16);

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public AccumulationGeometry()
    {
        var triangles = new List<TraceTriangle>();

        for (int x = -8; x < 8; x++)
        {
            var a = new Vector3(x + 0.495f, -8, 0);
            var b = new Vector3(x + 0.505f, -8, 0);
            var c = new Vector3(x + 0.505f, 8, 0);
            var d = new Vector3(x + 0.495f, 8, 0);
            triangles.Add(new(a, b, c, Vector3.Zero, new Vector3(6)));
            triangles.Add(new(a, c, d, Vector3.Zero, new Vector3(6)));
        }

        Triangles = triangles;
    }
}

/// <summary>Connects the accumulation geometry fixture to a path tracer.</summary>
internal sealed class AccumulationScene : Scene
{
    public PathTracer Tracer;
    public AntiAliasing AA;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        Tracer = ECS.AddSystem(new PathTracer { Geometry = new AccumulationGeometry(), MaxBounces = 1, ResolutionScale = 1, Environment = Vector3.Zero });

        var tone = ECS.AddSystem(new ToneMapping { Input = Tracer.GetOutput, Mapper = ToneMapper.None, Exposure = 2, After = [Tracer] });
        AA = ECS.AddSystem(new AntiAliasing { Input = tone.GetOutput, Mode = PostAAMode.TAA, Camera = () => Tracer.Geometry.Camera, JitterEnabled = false, After = [tone] });
        base.SetupECS();
    }
}

/// <summary>Runs accumulation regression checks and records diagnostic frames.</summary>
internal sealed class AccumulationRegressionWindow : Window
{
    private readonly AccumulationScene Scene = new();
    private int Frame;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public AccumulationRegressionWindow() : base(new GameLoop(), new() { ShowLoader = false, Title = "Radiant accumulation regression", Width = 320, Height = 180 })
    {
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
        GameLoop.SetTargetFps(0);
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        Frame++;

        if (Frame == 16385)
        {
            var first = Read(Scene.Tracer.GetOutput());

            if (Scene.Tracer.SampleCount != 1 || first.Any(p => p.X != 0 && p.X != 6)) throw new Exception("Reset retained old accumulation.");

            Console.WriteLine("PASS: rare HDR samples retain expected brightness beyond the accumulation window, with exposure/TAA and history reset.");
            Exit();

            return;
        }
        if (Frame is not (1024 or 4096 or 8192 or 16384)) return;

        var pixels = Read(Scene.Tracer.GetOutput());
        double mean = pixels.Average(p => p.X);
        double processedMean = Read(Scene.AA.GetOutput()).Average(p => p.X);
        Console.WriteLine($"Accumulation frame {Frame}: radiance={mean:F6} (expected 0.06), exposure + TAA={processedMean:F6} (expected 0.12)");

        if (pixels.Any(p => !float.IsFinite(p.X) || p.X < 0 || Math.Abs(p.Y - p.X) > 0.002f || Math.Abs(p.Z - p.X) > 0.002f || p.W != 1)) throw new Exception("Invalid accumulation pixels.");
        if (Frame != 16384) return;
        if (Math.Abs(mean - 0.06) > 0.006 || Math.Abs(processedMean - 0.12) > 0.012) throw new Exception($"Long-running accumulation changed brightness: raw={mean:F6}, processed={processedMean:F6}.");

        Scene.Tracer.ResetAccumulation();

        if (Scene.Tracer.SampleCount != 0) throw new Exception("History reset failed.");
    }

    /// <summary>Reads a rendered pixel or image for the current regression assertion.</summary>
    private static Vector4[] Read(Texture2D target)
    {
        Vector4[] pixels;

        if (target.Format == SurfaceFormat.HalfVector4)
        {
            var packed = new HalfVector4[target.Width * target.Height];
            target.GetData(packed);
            pixels = packed.Select(p => p.ToVector4().ToNumerics()).ToArray();
        }
        else
        {
            pixels = new Vector4[target.Width * target.Height];
            target.GetData(pixels);
        }

        return pixels;
    }
}
