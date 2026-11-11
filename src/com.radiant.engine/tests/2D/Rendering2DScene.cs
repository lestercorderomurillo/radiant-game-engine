using System;
using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;
using com.radiant.engine.input;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>The same image systems as Cornell Box, using only compact 2D entity components.</summary>
public class Rendering2DScene : Scene
{
    public ShapeGeometry2D Geometry { get; private set; }
    public PTGI Tracer { get; private set; }
    public Denoiser Denoiser { get; private set; }
    public LightingReconstructor Lighting { get; private set; }
    public RasterRenderer Raster { get; private set; }
    public Upscaler Upscaler { get; private set; }
    public AntiAliaser AA { get; private set; }
    public RasterSampler Sampling { get; private set; }
    public ToneMapper ToneMap { get; private set; }
    public RenderInspector Pipeline { get; private set; }
    public int RenderMethod { get; private set; }

    private FlatRenderer Flat;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        ECS.AddSystemCollection(SystemCollections.Debugging);

        Sampling = ECS.AddSystem<RasterSampler>();
        Geometry = ECS.AddSystem(new ShapeGeometry2D { Sampling = Sampling });

        Flat = ECS.AddSystem(new FlatRenderer { Input = Geometry.GetOutput, After = [Geometry] });
        Raster = ECS.AddSystem(new RasterRenderer { Geometry = Geometry, Input = Geometry.GetOutput, After = [Geometry] }, false);
        Tracer = ECS.AddSystem(new PTGI { SeparateLighting = true, Geometry = Geometry, Channels = TraceChannels.All, ViewSize = () => Geometry.ViewSize, After = [Geometry], Environment = Vector3.One }, false);
        Denoiser = ECS.AddSystem(new Denoiser { TraceSource = Tracer, Input = () => Tracer.Enabled ? Tracer.GetOutput() : null, Albedo = () => Tracer.AlbedoTexture, Normals = () => Tracer.NormalTexture, Depth = () => Tracer.DepthTexture, Emission = () => Tracer.EmissionTexture, SingleSample = () => Tracer.SampleTexture, Camera = () => Tracer.Geometry.HasCamera ? Tracer.Geometry.Camera : null, Revision = () => Tracer.RadianceRevision, After = [Tracer] });
        Lighting = ECS.AddSystem(new LightingReconstructor { Source = Tracer, Input = Denoiser.GetOutput, After = [Tracer, Denoiser] });

        ToneMap = ECS.AddSystem(new ToneMapper { Input = GetRenderedOutput, Operator = ToneMapOperator.None, After = [Flat, Raster, Tracer, Denoiser, Lighting] });
        AA = ECS.AddSystem(new AntiAliaser { Input = ToneMap.GetOutput, Mode = PostAAMode.None, OutputSize = () => Geometry.ViewSize, Camera = () => Geometry.Camera, Depth = () => RenderMethod == 2 ? Lighting.DepthTexture ?? Tracer.DepthTexture : Geometry.DepthTexture, GeometryRevision = () => Geometry.Revision, After = [ToneMap] });
        AA.SetSampling(Sampling);
        Upscaler = ECS.AddSystem(new Upscaler { Input = AA.GetOutput, After = [AA] });

        ECS.AddSystem(new ToneMapperSettings { Id = "rendering2dColor", Source = ToneMap });
        ECS.AddSystem(new AntiAliaserSettings { Id = "rendering2dAA", Source = AA, Sampling = Sampling, Upscaler = Upscaler, Title = "Image quality" });
        ECS.AddSystem(new PTGISettings { Id = "rendering2dPath", Source = Tracer, Denoiser = Denoiser, Changed = AA.ResetHistory });

        Pipeline = ECS.AddSystem(new RenderInspector { Id = "rendering2d", Title = "2D Pipeline", Input = Upscaler.GetOutput, After = [Upscaler] });

        Pipeline.AddSource("Geometry", Geometry);
        Pipeline.AddSource("Raster", Raster);
        Pipeline.AddSource("Path tracing", Tracer);
        Pipeline.AddSource("Denoising", Denoiser);
        Pipeline.AddSource("Path lighting", Lighting);
        Pipeline.AddSource("Reconstruction", Upscaler);
        Pipeline.AddSource("Tone mapping", ToneMap);
        Pipeline.AddSource("AA", AA);

        ECS.AddSystem(new RenderPresenter { Input = Pipeline.GetOutput, ClearColor = new Color(18, 22, 30), Blend = BlendState.AlphaBlend, After = [Pipeline] });
    }

    /// <inheritdoc/>
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

    /// <summary>Returns the image from the currently selected flat, raster, or path-traced renderer.</summary>
    public Texture2D GetRenderedOutput() => RenderMethod switch { 1 => Raster.GetOutput(), 2 => Lighting.GetOutput(), _ => Flat.GetOutput() };

    /// <summary>Activates the selected renderer and resets dependent image history and inspection.</summary>
    public void SelectRenderer(int index)
    {
        if (index < 0 || index > 2) throw new ArgumentOutOfRangeException(nameof(index));

        Geometry.Rasterize = index != 2;
        AA.JitterEnabled = index != 2;
        AA.ResetHistory();
        Upscaler.ResetHistory();

        Raster.Enabled = index == 1;
        Tracer.Enabled = index == 2;

        if (index != 1) Raster.Dispose();
        if (index != 2) Tracer.Dispose();

        Tracer.ResetAccumulation();
        RenderMethod = index;
        Pipeline.SelectBuffer(0);
        Inspector.SetDropdownValue("rendering2d", "renderer", index);
    }

    /// <inheritdoc/>
    public override void Update()
    {
        Geometry.Resolution = Upscaler.ResolveViewSize(Geometry.Resolution);
        Geometry.CaptureDepth = RenderMethod != 2 && AA.Enabled && AA.Mode == PostAAMode.TAA;


        if (Renderer.IsActive && Controls.WasPressed(Key.Tab)) Pipeline.SelectNextBuffer();
        if (Renderer.IsActive && Controls.WasPressed(Key.R)) SelectRenderer((RenderMethod + 1) % 3);
    }
}
