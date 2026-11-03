using System;
using System.IO;
using System.Linq;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

internal sealed class PipelineRegressionWindow : Window
{
    private readonly Scene Scene;
    private int Frame;
    private Color[] FlatPixels;
    private int InitialBuilds;
    private readonly string Dimension;
    private PathTracer Tracer => Scene is CornellBoxScene s ? s.Tracer : ((Rendering2DScene)Scene).Tracer;
    private RasterRenderer Raster => Scene is CornellBoxScene s ? s.Raster : ((Rendering2DScene)Scene).Raster;
    private AntiAliasing AA => Scene is CornellBoxScene s ? s.AA : ((Rendering2DScene)Scene).AA;
    private ToneMapping ToneMap => Scene is CornellBoxScene s ? s.ToneMap : ((Rendering2DScene)Scene).ToneMap;
    private void Select(int method) { if (Scene is CornellBoxScene s) s.SelectRenderer(method); else ((Rendering2DScene)Scene).SelectRenderer(method); }
    public PipelineRegressionWindow(Scene scene) : base(new GameLoop(), new() { Title = "Radiant pipeline regression", Width = 640, Height = 360 })
    {
        Scene = scene; Dimension = scene is CornellBoxScene ? "3d" : "2d";
        GameLoop.AddScene(scene); GameLoop.ThrottleUnfocused = false;
    }
    protected override void Update(GameTime time)
    {
        base.Update(time);
        // Update after the scene so a controlled camera mutation lasts through this frame's render.
        if (Frame == 5) Select(1);
        if (Frame == 10) { AA.Enabled = true; AA.Mode = PostAAMode.FXAA; }
        if (Frame == 15) ToneMap.Mapper = ToneMapper.Reinhard;
        if (Frame == 20) ToneMap.Mapper = ToneMapper.ACES;
        if (Frame == 25) { Tracer.ResolutionScale = 0.25f; Select(2); }
        if (Frame == 32 && Scene is CornellBoxScene cornell)
        {
            ref var camera = ref Scene.ECS.GetComponent<Camera3D>(cornell.Meshes.CameraEntity);
            camera.Position.X += 0.2f;
        }
        if (Frame == 35) Tracer.Environment += new Vector3(0.1f);
        if (Frame == 40)
        {
            if (Scene is CornellBoxScene c) Scene.ECS.GetComponent<Transform3D>(c.Meshes.Instances[0].Entity).Position.X += 0.1f;
            else
            {
                var ids = new System.Collections.Generic.List<int>(); Scene.ECS.GetAllEntityIds(ids);
                Scene.ECS.GetComponent<Transform2D>(ids.First(id => Scene.ECS.HasComponent<Transform2D>(id))).Position.X += 1;
            }
        }
        if (Frame == 45)
        {
            GraphicsDeviceManager.PreferredBackBufferWidth = 800; GraphicsDeviceManager.PreferredBackBufferHeight = 500;
            GraphicsDeviceManager.ApplyChanges();
        }
        if (Frame == 48)
        {
            if (Scene is CornellBoxScene c) c.Meshes.CameraEntity = int.MaxValue;
            else ((Rendering2DScene)Scene).Geometry.CameraEntity = int.MaxValue;
        }
        if (Frame == 50)
        {
            if (Scene is CornellBoxScene c) c.Meshes.CameraEntity = -1;
            else ((Rendering2DScene)Scene).Geometry.CameraEntity = -1;
        }
        if (Frame == 60) Select(0);
        if (Frame == 65) Select(2);
    }
    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        Frame++;
        if (Frame == 5)
        {
            var flat = Scene is CornellBoxScene c ? c.Geometry.GetOutput() : ((Rendering2DScene)Scene).Geometry.GetOutput();
            FlatPixels = Pixels(flat);
            if (FlatPixels.Count(p => p.A > 0) < 1000) throw new Exception("Flat geometry is empty.");
            if (Tracer.BvhBuildCount != 0 || Tracer.GetOutput() != null) throw new Exception("Inactive tracer allocated tracing resources.");
            if (!ReferenceEquals(AA.GetOutput(), flat)) throw new Exception("Disabled image stages must bypass their input.");
            Save("flat", flat);
        }
        if (Frame == 10)
        {
            var pixels = Pixels(Raster.GetOutput());
            if (!pixels.Where((p, i) => p != FlatPixels[i]).Any()) throw new Exception("Raster lighting did not change flat output.");
            Save("raster", Raster.GetOutput());
        }
        if (Frame == 15)
        {
            var before = Pixels(ToneMap.GetOutput()); var after = Pixels(AA.GetOutput());
            if (!after.Where((p, i) => p != before[i]).Any()) throw new Exception("FXAA did not filter any edges.");
            Save("aa", AA.GetOutput());
        }
        if (Frame is 20 or 25)
        {
            var input = Pixels(Raster.GetOutput()); var mapped = Pixels(ToneMap.GetOutput());
            if (!mapped.Where((pixel, index) => pixel != input[index]).Any()) throw new Exception("Tonemapping was not applied.");
            Save(Frame == 20 ? "reinhard" : "aces", ToneMap.GetOutput());
        }
        if (Frame == 30)
        {
            if (Tracer.SampleCount < 4 || Tracer.NodeCount < 1 || Tracer.BvhBuildCount != 1) throw new Exception($"Progressive tracing failed: samples={Tracer.SampleCount}, builds={Tracer.BvhBuildCount}, nodes={Tracer.NodeCount}.");
            if (Scene is CornellBoxScene c && c.Geometry.ColorTexture != null) throw new Exception("Path-only view retained raster geometry targets.");
            if (!Pixels(Tracer.GetOutput()).Any(p => p.R > 2 || p.G > 2 || p.B > 2)) throw new Exception("Tracer output is empty.");
            var traced = Pixels(Tracer.GetOutput());
            if (traced.Distinct().Count() < 8) throw new Exception("Path tracer missed scene geometry: output is uniform.");
            if (Scene is Rendering2DScene)
            {
                var target = Tracer.GetOutput();
                var center = traced[(int)(target.Height * 85f / 180) * target.Width + (int)(target.Width * 200f / 320)];
                if (Math.Abs(center.G - Color.Coral.G) > 6 || Math.Abs(center.B - Color.Coral.B) > 6) throw new Exception($"2D path intersection produced {center} instead of the coral circle.");
            }
            InitialBuilds = Tracer.BvhBuildCount;
        }
        if (Frame == 33 && Scene is CornellBoxScene && Tracer.SampleCount != 1) throw new Exception("Camera changes did not reset accumulation.");
        if (Frame == 36 && Tracer.SampleCount != 1) throw new Exception("Environment changes did not reset accumulation.");
        if (Frame == 41 && (Tracer.SampleCount != 1 || Tracer.BvhBuildCount != InitialBuilds + 1)) throw new Exception("Geometry changes did not rebuild the BVH and reset accumulation.");
        if (Frame == 49 && (Tracer.SampleCount != 0 || Pixels(Tracer.GetOutput()).Any(p => p.A != 0))) throw new Exception("Missing camera retained a stale path tracing image.");
        if (Frame == 49)
        {
            foreach (var target in new[] { Tracer.AlbedoTexture, Tracer.NormalTexture, Tracer.EmissionTexture })
                if (Pixels(target).Any(p => p.A != 0)) throw new Exception("Missing camera retained stale path surface attributes.");
            var depths = new float[Tracer.DepthTexture.Width * Tracer.DepthTexture.Height]; Tracer.DepthTexture.GetData(depths);
            if (depths.Any(d => d != 0)) throw new Exception("Missing camera retained stale path depth.");
        }
        if (Frame == 55)
        {
            if (Tracer.GetOutput().Width != 200 || Tracer.GetOutput().Height != 125) throw new Exception("Tracer resolution did not follow resize.");
            if (Tracer.DepthTexture.Width != 200 || Tracer.NormalTexture.Height != 125) throw new Exception("Path surface buffers did not resize.");
            Save("path", Tracer.GetOutput()); Save("path-final", AA.GetOutput());
        }
        if (Frame == 61 && (Tracer.GetOutput() != null || Tracer.DepthTexture != null || Tracer.AlbedoTexture != null || Tracer.NormalTexture != null || Tracer.EmissionTexture != null)) throw new Exception("Switching to flat retained tracer output resources.");
        if (Frame == 70)
        {
            if (Tracer.SampleCount < 4) throw new Exception("Tracer did not resume after switching back.");
            Console.WriteLine($"PASS: {Dimension} flat/raster/path tracing, optional FXAA, tonemappers, lazy resources, camera/geometry/environment invalidation, BVH reuse, switching and resize.");
            Exit();
        }
    }
    private static Color[] Pixels(Texture2D texture)
    {
        if (texture.Format == SurfaceFormat.Color) { var data = new Color[texture.Width * texture.Height]; texture.GetData(data); return data; }
        if (texture.Format == SurfaceFormat.HalfVector4)
        {
            var data = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[texture.Width * texture.Height]; texture.GetData(data);
            return data.Select(p => new Color(p.ToVector4())).ToArray();
        }
        if (texture.Format == SurfaceFormat.Vector4)
        {
            var data = new Vector4[texture.Width * texture.Height]; texture.GetData(data);
            return data.Select(p => new Color(p)).ToArray();
        }
        throw new Exception($"Unexpected test texture format {texture.Format}.");
    }
    private void Save(string suffix, Texture2D texture)
    {
        Directory.CreateDirectory("artifacts/pipeline");
        using var stream = File.Create($"artifacts/pipeline/{Dimension}-{suffix}.png");
        if (texture.Format == SurfaceFormat.Vector4)
        {
            using var preview = new Texture2D(texture.GraphicsDevice, texture.Width, texture.Height);
            preview.SetData(Pixels(texture));
            preview.SaveAsPng(stream, preview.Width, preview.Height);
        }
        else texture.SaveAsPng(stream, texture.Width, texture.Height);
    }
}
