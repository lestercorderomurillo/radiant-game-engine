using System;
using System.IO;
using System.Linq;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

/// <summary>Runs aaregression checks and records diagnostic frames.</summary>
internal sealed class AARegressionWindow : Window
{
    private readonly Scene Scene;
    private int Frame;
    private readonly int Method;
    private bool Inspecting, InspectedNormals, InspectedDepth;
    private int Inspected;
    private readonly (RasterAAMode Geometry, PostAAMode Post)[] Cases = {
        (RasterAAMode.None, PostAAMode.None), (RasterAAMode.None, PostAAMode.FXAA), (RasterAAMode.None, PostAAMode.SMAA), (RasterAAMode.None, PostAAMode.TAA),
        (RasterAAMode.MSAA2x, PostAAMode.None), (RasterAAMode.MSAA4x, PostAAMode.None), (RasterAAMode.MSAA8x, PostAAMode.None),
        (RasterAAMode.SSAA2x, PostAAMode.None), (RasterAAMode.SSAA3x, PostAAMode.None), (RasterAAMode.MSAA4x, PostAAMode.SMAA), (RasterAAMode.SSAA2x, PostAAMode.TAA)
    };
    private AntiAliasing AA => Scene is CornellBoxScene c ? c.AA : ((Rendering2DScene)Scene).AA;
    private RasterSampling Sampling => Scene is CornellBoxScene c ? c.Sampling : ((Rendering2DScene)Scene).Sampling;
    private ToneMapping Tone => Scene is CornellBoxScene c ? c.ToneMap : ((Rendering2DScene)Scene).ToneMap;
    private Texture2D GeometryColor => Scene is CornellBoxScene c ? c.Geometry.ColorTexture : ((Rendering2DScene)Scene).Geometry.ColorTexture;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public AARegressionWindow(bool twoD, int method = 0) : base(new GameLoop(), new() { Title = "Radiant AA regression", Width = 640, Height = 360 })
    {
        Method = method;
        Scene = twoD ? new Rendering2DScene() : new CornellBoxScene();
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
    }

    /// <inheritdoc/>
    protected override void Update(GameTime time)
    {
        base.Update(time);

        if (Inspecting) return;

        int caseIndex = Frame / 8;

        if (Frame % 8 != 0) return;

        if (Frame == 0) { if (Scene is CornellBoxScene cornell) cornell.SelectRenderer(Method); else ((Rendering2DScene)Scene).SelectRenderer(Method); }

        if (caseIndex < Cases.Length)
        {
            var sample = Cases[caseIndex];
            Sampling.Mode = sample.Geometry;
            AA.Mode = sample.Post;
            AA.Enabled = true;
            AA.ResetHistory();
            if (Scene is Rendering2DScene s) s.Geometry.CaptureDepth = sample.Post == PostAAMode.TAA;
        }
        else
        {
            Sampling.Mode = RasterAAMode.None;
            AA.Mode = PostAAMode.None;
            Tone.Mapper = ToneMapper.None;
            Tone.Exposure = caseIndex == Cases.Length ? 0.5f : 2;

            if (caseIndex >= Cases.Length + 2)
            {
                Tone.Exposure = 1;
                AA.Mode = caseIndex == Cases.Length + 2 ? PostAAMode.FXAA : PostAAMode.SMAA;

                var entities = new System.Collections.Generic.List<int>();
                Scene.ECS.GetAllEntityIds(entities);

                foreach (int entity in entities)
                {
                    if (Scene.ECS.HasComponent<Mesh3D>(entity)) Scene.ECS.GetComponent<Mesh3D>(entity).Color = Color.Black;
                    if (Scene.ECS.HasComponent<Material>(entity)) Scene.ECS.GetComponent<Material>(entity).Albedo = Color.Black;
                }
            }
        }
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        Frame++;

        if (Inspecting) { CheckInspection(); return; }
        if (Frame % 8 != 0) return;

        int index = Frame / 8 - 1;
        string dimension = Scene is CornellBoxScene ? "3d" : "2d";

        if (index < Cases.Length)
        {
            var test = Cases[index];
            var result = AA.GetOutput();

            if (result?.Width != 640 || result.Height != 360) throw new Exception("AA output resolution differs from the view.");
            if (Method != 2 && GeometryColor.Width != 640 * Sampling.Scale) throw new Exception("SSAA did not increase geometry resolution.");
            if (Method != 2 && GeometryColor is RenderTarget2D target && target.MultiSampleCount != Sampling.ActualSamples) throw new Exception("Reported MSAA count differs from actual target.");
            if (test.Post == PostAAMode.TAA && AA.HistoryFrames < 4) throw new Exception("TAA history did not accumulate.");

            var output = Read(result);

            if (output.Any(p => !float.IsFinite(p.X + p.Y + p.Z + p.W))) throw new Exception("AA produced non-finite pixels.");

            if (test.Post == PostAAMode.FXAA || test.Post == PostAAMode.SMAA)
            {
                var before = Read(AA.Buffers["Resolved"]() ?? Tone.GetOutput());

                if (!output.Where((p, i) => Vector4.Distance(p, before[i]) > 0.002f).Any()) throw new Exception($"{test.Post} did not filter any pixels.");
            }

            if (test.Post == PostAAMode.SMAA && !Read(AA.Buffers["SMAA weights"]()).Any(p => p.X + p.Y + p.Z + p.W > 0.001f)) throw new Exception("SMAA blending weights are empty.");
            if (Method != 2 && (Sampling.Scale > 1 || Sampling.ActualSamples > 1) && !output.Any(p => p.W > 0.01f && p.W < 0.99f)) throw new Exception("Geometry AA did not produce fractional edge coverage.");

            Directory.CreateDirectory("artifacts/aa");
            Save($"artifacts/aa/{dimension}-{Method}-{test.Geometry}-{test.Post}.png", result);
            Console.WriteLine($"PASS: {dimension} renderer={Method} {test.Geometry} + {test.Post}, actual samples={Sampling.ActualSamples}, TAA frames={AA.HistoryFrames}");
        }
        else if (index >= Cases.Length + 2)
        {
            var output = Read(AA.GetOutput());

            if (!output.Any(p => p.W > 0.01f && p.W < 0.99f)) throw new Exception($"{AA.Mode} did not filter black-on-transparent coverage edges.");
            if (output.Any(p => p.X + p.Y + p.Z > 0.001f)) throw new Exception("Coverage AA introduced color into a black silhouette.");

            Console.WriteLine($"PASS: {dimension} {AA.Mode} alpha-coverage edges without RGB contrast.");
            if (index == Cases.Length + 3) Exit();
        }
        else
        {
            var input = Read(Scene is CornellBoxScene c ? c.GetRenderedOutput() : ((Rendering2DScene)Scene).GetRenderedOutput());
            var mapped = Read(Tone.GetOutput());
            int compared = 0;

            for (int i = 0; i < input.Length; i++)
            {
                if (input[i].W < 0.99f) continue;

                var expected = new Vector3(input[i].X, input[i].Y, input[i].Z) * Tone.Exposure;

                if (Vector3.Distance(expected, new Vector3(mapped[i].X, mapped[i].Y, mapped[i].Z)) > Math.Max(0.005f, expected.Length() * 0.001f)) throw new Exception($"Exposure without a tonemapper produced {mapped[i]}, expected {expected}.");

                compared++;
            }

            if (compared < (Method == 2 ? 100 : 1000)) throw new Exception("Exposure comparison did not cover the scene.");

            Console.WriteLine($"PASS: {dimension} exposure={Tone.Exposure} without tonemapping, {compared} verified pixels.");
            if (Method == 2 && index == Cases.Length + 1) VerifyPathBuffers();

        }
    }

    /// <summary>Checks that requested path-tracing guides are present and correctly sized.</summary>
    private void VerifyPathBuffers()
    {
        var tracer = Scene is CornellBoxScene c ? c.Tracer : ((Rendering2DScene)Scene).Tracer;
        var inspector = Scene is CornellBoxScene box ? box.Pipeline : ((Rendering2DScene)Scene).Pipeline;

        if (tracer.GetOutput().Width != 320 || tracer.GetOutput().Height != 180) throw new Exception("Raster sampling leaked into tracing resolution.");

        foreach (var texture in new[] { tracer.AlbedoTexture, tracer.NormalTexture, tracer.DepthTexture, tracer.EmissionTexture })
            if (texture == null || texture.Width != 320 || texture.Height != 180) throw new Exception("Missing path surface buffer.");

        var depth = new float[320 * 180];
        tracer.DepthTexture.GetData(depth);

        if (!depth.Any(d => d > 0 && d < 1) || depth.Any(d => !float.IsFinite(d) || d < 0 || d > 1)) throw new Exception("Invalid path depth.");

        var albedo = Read(tracer.AlbedoTexture);

        if (!albedo.Any(p => p.W == 1 && p.X + p.Y + p.Z > 0)) throw new Exception("Empty path albedo.");

        var normalsPixels = Read(tracer.NormalTexture);
        var emissionPixels = Read(tracer.EmissionTexture);
        int tested = 0;
        var camera = tracer.Geometry.Camera;

        for (int y = 2; y < 180; y += 4)
            for (int x = 2; x < 320; x += 4)
            {
                var clip = new Vector4((x + 0.5f) * 2 / 320 - 1, 1 - (y + 0.5f) * 2 / 180, 0, 1);
                var near = Vector4.Transform(clip, camera.InverseViewProjection);
                near /= near.W;
                clip.Z = 1;

                var far = Vector4.Transform(clip, camera.InverseViewProjection);
                far /= far.W;

                var origin = new Vector3(near.X, near.Y, near.Z);
                var direction = Vector3.Normalize(new Vector3(far.X, far.Y, far.Z) - origin);
                float closest = Vector3.Distance(new Vector3(far.X, far.Y, far.Z), origin), margin = 0;
                bool found = false;
                TraceTriangle hit = default;

                foreach (var triangle in tracer.Geometry.Triangles)
                {
                    var e1 = triangle.B - triangle.A;
                    var e2 = triangle.C - triangle.A;
                    var p = Vector3.Cross(direction, e2);
                    float determinant = Vector3.Dot(e1, p);

                    if (Math.Abs(determinant) < 1e-8f) continue;

                    var t = origin - triangle.A;
                    float u = Vector3.Dot(t, p) / determinant;
                    var q = Vector3.Cross(t, e1);
                    float v = Vector3.Dot(direction, q) / determinant;
                    float distance = Vector3.Dot(e2, q) / determinant;

                    if (u < 0 || v < 0 || u + v > 1 || distance <= 0.0001f || distance >= closest) continue;

                    closest = distance;
                    margin = Math.Min(u, Math.Min(v, 1 - u - v));
                    found = true;
                    hit = triangle;
                }

                int index = y * 320 + x;

                if (!found)
                {
                    if (depth[index] != 0 || albedo[index].W != 0) throw new Exception("Path surface background retained geometry.");

                    continue;
                }
                if (margin < 0.02f) continue;

                var normal = Vector3.Normalize(Vector3.Cross(hit.B - hit.A, hit.C - hit.A));
                if (Vector3.Dot(normal, direction) > 0) normal = -normal;

                var encoded = new Vector4(normal * 0.5f + new Vector3(0.5f), 1);
                float expectedDepth = -Vector3.Transform(origin + direction * closest, camera.View).Z / camera.FarPlane;

                if (Vector4.Distance(albedo[index], new Vector4(hit.Albedo, 1)) > 0.008f || Vector4.Distance(normalsPixels[index], encoded) > 0.008f || Math.Abs(depth[index] - expectedDepth) > 0.0001f || Vector4.Distance(emissionPixels[index], new Vector4(hit.Emission, 1)) > 0.008f) throw new Exception($"Path surface mismatch at {x},{y}.");

                tested++;
            }

        if (tested < 100) throw new Exception("Path surface comparison did not cover enough geometry.");

        Console.WriteLine($"PASS: path surfaces match {tested} independent CPU rays, with raster-independent resolution.");
        Inspecting = true;
        inspector.SelectBuffer(0);
        inspector.SelectNextBuffer();
    }

    /// <summary>Checks that inspecting a buffer preserves the rendering pipeline's output contract.</summary>
    private void CheckInspection()
    {
        var inspector = Scene is CornellBoxScene box ? box.Pipeline : ((Rendering2DScene)Scene).Pipeline;
        var tracer = Scene is CornellBoxScene c ? c.Tracer : ((Rendering2DScene)Scene).Tracer;

        if (inspector.GetOutput() == null || inspector.GetOutput().IsDisposed) throw new Exception("Buffer cycle selected an unavailable texture.");
        if (inspector.BufferName.StartsWith("Geometry /")) throw new Exception("Inactive raster buffer remains selectable.");

        InspectedNormals |= inspector.BufferName == "Path tracing / Normals";
        InspectedDepth |= inspector.BufferName == "Path tracing / Depth";

        if (inspector.BufferName == "Path tracing / Depth")
        {
            var depth = new float[tracer.DepthTexture.Width * tracer.DepthTexture.Height];
            tracer.DepthTexture.GetData(depth);

            var preview = Read(inspector.GetOutput());

            if (preview.Where((p, i) => Math.Abs(p.X - depth[i]) > 0.005f || p.X != p.Y || p.Y != p.Z).Any()) throw new Exception("Depth inspection does not match scalar source data.");
        }

        if (inspector.BufferName is "Path tracing / Albedo" or "Path tracing / Normals" or "Path tracing / Depth" or "Path tracing / Emission") Save($"artifacts/aa/{(Scene is CornellBoxScene ? "3d" : "2d")}-path-{inspector.BufferName.Split(" / ")[1]}.png", inspector.GetOutput());

        if (++Inspected > inspector.BufferCount) throw new Exception("Buffer cycle failed to wrap.");

        inspector.SelectNextBuffer();

        if (inspector.BufferIndex != 0) return;
        if (!InspectedNormals || !InspectedDepth) throw new Exception("Path surface buffers missing from inspection.");

        Console.WriteLine("PASS: available-buffer cycling and rendered depth preview match the source data.");
        Exit();
    }

    /// <summary>Writes the current test image to its diagnostic output path.</summary>
    private static void Save(string path, Texture2D texture)
    {
        using var stream = File.Create(path);

        if (texture.Format == SurfaceFormat.Vector4)
        {
            using var preview = new Texture2D(texture.GraphicsDevice, texture.Width, texture.Height);
            preview.SetData(Read(texture).Select(p => new Color(p)).ToArray());
            preview.SaveAsPng(stream, preview.Width, preview.Height);
        }
        else texture.SaveAsPng(stream, texture.Width, texture.Height);
    }

    /// <summary>Reads a rendered pixel or image for the current regression assertion.</summary>
    private static Vector4[] Read(Texture2D texture)
    {
        if (texture.Format == SurfaceFormat.Vector4) { var data = new Vector4[texture.Width * texture.Height]; texture.GetData(data); return data; }
        if (texture.Format == SurfaceFormat.Color) { var data = new Color[texture.Width * texture.Height]; texture.GetData(data); return data.Select(p => p.ToVector4()).ToArray(); }
        if (texture.Format == SurfaceFormat.HalfVector4) { var data = new HalfVector4[texture.Width * texture.Height]; texture.GetData(data); return data.Select(p => p.ToVector4().ToNumerics()).ToArray(); }

        throw new Exception($"Unsupported AA regression texture {texture.Format}");
    }
}
