using System;
using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>The same image systems as Cornell Box, using only compact 2D entity components.</summary>
public class Rendering2DScene : Scene
{
    public ShapeGeometry2D Geometry { get; private set; }
    public PathTracer Tracer { get; private set; }
    public Denoising Denoiser { get; private set; }
    public RasterRenderer Raster { get; private set; }
    public AntiAliasing AA { get; private set; }
    public RasterSampling Sampling { get; private set; }
    public ToneMapping ToneMap { get; private set; }
    public RenderInspector Pipeline { get; private set; }
    public int RenderMethod { get; private set; }
    private FlatRenderer Flat;
    private KeyboardState Previous;
    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<PerformanceMonitor>();
        Sampling = ECS.AddSystem<RasterSampling>();
        Geometry = ECS.AddSystem(new ShapeGeometry2D { Sampling = Sampling });
        Flat = ECS.AddSystem(new FlatRenderer { Input = Geometry.GetOutput, After = [Geometry] });
        Raster = ECS.AddSystem(new RasterRenderer { Geometry = Geometry, Input = Geometry.GetOutput, After = [Geometry] }, false);
        Tracer = ECS.AddSystem(new PathTracer { Geometry = Geometry, Channels = TraceChannels.All, ViewSize = () => Geometry.ViewSize, After = [Geometry], Environment = Vector3.One }, false);
        Denoiser = ECS.AddSystem(new Denoising { Input = () => Tracer.Enabled ? Tracer.GetOutput() : null, Albedo = () => Tracer.AlbedoTexture, Normals = () => Tracer.NormalTexture, Depth = () => Tracer.DepthTexture, Emission = () => Tracer.EmissionTexture, SingleSample = () => Tracer.SampleTexture, Camera = () => Tracer.Geometry.HasCamera ? Tracer.Geometry.Camera : null, Revision = () => Tracer.RadianceRevision, After = [Tracer] });
        ToneMap = ECS.AddSystem(new ToneMapping { Input = GetRenderedOutput, Mapper = ToneMapper.None, After = [Flat, Raster, Tracer, Denoiser] });
        AA = ECS.AddSystem(new AntiAliasing { Input = ToneMap.GetOutput, Mode = PostAAMode.None, OutputSize = () => Geometry.ViewSize, Camera = () => Geometry.Camera, Depth = () => RenderMethod == 2 ? Tracer.DepthTexture : Geometry.DepthTexture, GeometryRevision = () => Geometry.Revision, After = [ToneMap] });
        AA.SetSampling(Sampling);
        ECS.AddSystem(new ColorSettings { Id = "rendering2dColor", Source = ToneMap });
        ECS.AddSystem(new AntiAliasingSettings { Id = "rendering2dAA", Source = AA, Sampling = Sampling });
        ECS.AddSystem(new PathTracingSettings { Id = "rendering2dPath", Source = Tracer, Denoiser = Denoiser, Changed = AA.ResetHistory });
        Pipeline = ECS.AddSystem(new RenderInspector { Id = "rendering2d", Title = "2D Pipeline", Input = AA.GetOutput, After = [AA] });
        Pipeline.AddSource("Geometry", Geometry); Pipeline.AddSource("Raster", Raster); Pipeline.AddSource("Path tracing", Tracer); Pipeline.AddSource("Denoising", Denoiser); Pipeline.AddSource("Tone mapping", ToneMap); Pipeline.AddSource("AA", AA);
        ECS.AddSystem(new RenderPresentation { Input = Pipeline.GetOutput, ClearColor = new Color(18, 22, 30), Blend = BlendState.AlphaBlend, After = [Pipeline] });
        base.SetupECS();
    }
    public override void SetupScene()
    {
        Renderer.FitWorld(new Vector2(160, 90), new Vector2(320, 180));
        int rectangle = ECS.CreateEntity(new Vector2(60, 55));
        ECS.GetComponent<Transform2D>(rectangle).Rotation = -0.25f;
        ECS.AddComponent<Rectangle2D>(rectangle) = new Rectangle2D { Size = new Vector2(85, 60) };
        ECS.AddComponent<Material>(rectangle) = new Material { Albedo = Color.CornflowerBlue, Emissive = Color.Black };
        int circle = ECS.CreateEntity(new Vector2(200, 85), 1);
        ECS.AddComponent<Circle2D>(circle) = new Circle2D { Radius = 35 };
        ECS.AddComponent<Material>(circle) = new Material { Albedo = Color.Coral, Emissive = Color.Black };
        int triangle = ECS.CreateEntity(new Vector2(125, 105), 2);
        ECS.AddComponent<Triangle2D>(triangle) = new Triangle2D { Size = new Vector2(65, 50) };
        ECS.AddComponent<Material>(triangle) = new Material { Albedo = Color.Gold, Emissive = Color.Black };
        Inspector.AddDropdown("rendering2d", "renderer", "Renderer", new[] { "Flat", "Raster", "Path tracing" }, 0, SelectRenderer);
    }
    public Texture2D GetRenderedOutput() => RenderMethod switch { 1 => Raster.GetOutput(), 2 => Denoiser.GetOutput(), _ => Flat.GetOutput() };
    public void SelectRenderer(int index)
    {
        if (index < 0 || index > 2) throw new ArgumentOutOfRangeException(nameof(index));
        Geometry.Rasterize = index != 2;
        AA.JitterEnabled = index != 2;
        AA.ResetHistory();
        Raster.Enabled = index == 1; Tracer.Enabled = index == 2;
        if (index != 1) Raster.Dispose();
        if (index != 2) Tracer.Dispose();
        Tracer.ResetAccumulation(); RenderMethod = index; Pipeline.SelectBuffer(0);
        Inspector.SetDropdownValue("rendering2d", "renderer", index);
    }
    public override void Update()
    {
        Geometry.CaptureDepth = RenderMethod != 2 && AA.Enabled && AA.Mode == PostAAMode.TAA;
        var keyboard = Keyboard.GetState();
        if (Renderer.IsActive && keyboard.IsKeyDown(Keys.Tab) && Previous.IsKeyUp(Keys.Tab)) Pipeline.SelectNextBuffer();
        if (Renderer.IsActive && keyboard.IsKeyDown(Keys.R) && Previous.IsKeyUp(Keys.R)) SelectRenderer((RenderMethod + 1) % 3);
        Previous = keyboard;
    }
}
