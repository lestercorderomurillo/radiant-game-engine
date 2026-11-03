using System;
using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.core;

public class CornellBoxScene : Scene
{
    public int InitialRenderMethod { get; init; }
    public DenoiserMode InitialDenoiser { get; init; }
    public MeshGeometry3D Meshes { get; private set; }
    public Geometry3D Geometry { get; private set; }
    public RasterRenderer Raster { get; private set; }
    public PathTracer Tracer { get; private set; }
    public Denoising Denoiser { get; private set; }
    public AntiAliasing AA { get; private set; }
    public RasterSampling Sampling { get; private set; }
    public ToneMapping ToneMap { get; private set; }
    public RenderInspector Pipeline { get; private set; }
    public int RenderMethod { get; private set; }
    private FlatRenderer Flat;
    private int Camera;
    private float Angle;
    private float Distance = 11;
    private KeyboardState PreviousKeyboard;

    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<PerformanceMonitor>();
        Sampling = ECS.AddSystem<RasterSampling>();
        Meshes = ECS.AddSystem(new MeshGeometry3D { Sampling = Sampling });
        Geometry = ECS.AddSystem(new Geometry3D { Channels = GeometryChannels.All, Source = Meshes, After = [Meshes] });
        Flat = ECS.AddSystem(new FlatRenderer { Input = Geometry.GetOutput, After = [Geometry] });
        Raster = ECS.AddSystem(new RasterRenderer { Geometry = Geometry, Input = Geometry.GetOutput, After = [Geometry] }, false);
        Tracer = ECS.AddSystem(new PathTracer { Geometry = Meshes, Channels = TraceChannels.All, ViewSize = () => Meshes.ViewSize, After = [Meshes] }, false);
        Denoiser = ECS.AddSystem(new Denoising { Mode = InitialDenoiser, Input = () => Tracer.Enabled ? Tracer.GetOutput() : null, Albedo = () => Tracer.AlbedoTexture, Normals = () => Tracer.NormalTexture, Depth = () => Tracer.DepthTexture, Emission = () => Tracer.EmissionTexture, SingleSample = () => Tracer.SampleTexture, Camera = () => Tracer.Geometry.HasCamera ? Tracer.Geometry.Camera : null, Revision = () => Tracer.RadianceRevision, After = [Tracer] }, enabled: true);
        ToneMap = ECS.AddSystem(new ToneMapping { Input = GetRenderedOutput, Mapper = ToneMapper.None, After = [Flat, Raster, Tracer, Denoiser] });
        AA = ECS.AddSystem(new AntiAliasing { Input = ToneMap.GetOutput, Mode = PostAAMode.None, OutputSize = () => Meshes.ViewSize, Camera = () => Meshes.Camera, Depth = () => RenderMethod == 2 ? Tracer.DepthTexture : Geometry.DepthTexture, GeometryRevision = () => Meshes.Revision, After = [ToneMap] });
        AA.SetSampling(Sampling);
        ECS.AddSystem(new ColorSettings { Id = "geometry3dColor", Source = ToneMap });
        ECS.AddSystem(new AntiAliasingSettings { Id = "geometry3dAA", Source = AA, Sampling = Sampling });
        ECS.AddSystem(new PathTracingSettings { Id = "pathTracing", Source = Tracer, Denoiser = Denoiser, Changed = AA.ResetHistory });
        Pipeline = ECS.AddSystem(new RenderInspector { Id = "geometry3d", Title = "3D Pipeline", Input = AA.GetOutput, After = [AA] });
        Pipeline.AddSource("Geometry", Geometry);
        Pipeline.AddSource("Raster", Raster);
        Pipeline.AddSource("Path tracing", Tracer);
        Pipeline.AddSource("Denoising", Denoiser);
        Pipeline.AddSource("Tone mapping", ToneMap);
        Pipeline.AddSource("AA", AA);
        ECS.AddSystem(new RenderPresentation { Input = Pipeline.GetOutput, ClearColor = new Color(18, 22, 30), Blend = Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend, After = [Pipeline] });
        base.SetupECS();
    }

    public override void SetupScene()
    {
        Camera = ECS.CreateEntity();
        ECS.AddComponent<Camera3D>(Camera) = new Camera3D { FarPlane = 40 };
        Meshes.CameraEntity = Camera;
        Inspector.AddDropdown("geometry3d", "renderer", "Renderer", new[] { "Flat", "Raster", "Path tracing" }, 0, SelectRenderer);
        Inspector.AddDropdown("geometry3d", "fpsCap", "FPS Limit", runtime.GameLoop.FpsOptionNames, 2, index => Renderer.GameLoop.SetTargetFps(runtime.GameLoop.FpsOptions[index]));
        Inspector.AddToggle("geometry3d", "throttleUnfocused", "Limit FPS when unfocused", Renderer.GameLoop.ThrottleUnfocused, enabled => Renderer.GameLoop.ThrottleUnfocused = enabled);
        var white = new Color(210, 210, 200);
        var roomSize = new Vector3(6, 1, 6);
        // Open front (+Z); room surfaces face inward for future material/lighting passes.
        AddMesh(Mesh.Plane, Vector3.Zero, roomSize, white);
        AddMesh(Mesh.Plane, new Vector3(0, 6, 0), roomSize, white, new Vector3(0, 0, MathHelper.Pi));
        AddMesh(Mesh.Plane, new Vector3(0, 3, -3), roomSize, white, new Vector3(MathHelper.PiOver2, 0, 0));
        AddMesh(Mesh.Plane, new Vector3(-3, 3, 0), roomSize, new Color(165, 32, 28), new Vector3(0, 0, -MathHelper.PiOver2));
        AddMesh(Mesh.Plane, new Vector3(3, 3, 0), roomSize, new Color(35, 135, 48), new Vector3(0, 0, MathHelper.PiOver2));
        AddMesh(Mesh.Cube, new Vector3(-1.1f, 0.9f, 1), new Vector3(1.7f, 1.8f, 1.7f), new Color(235, 235, 225), new Vector3(0, -0.3f, 0));
        AddMesh(Mesh.Cube, new Vector3(0.95f, 1.6f, -0.9f), new Vector3(1.8f, 3.2f, 1.8f), new Color(235, 235, 225), new Vector3(0, 0.3f, 0));

        int emitter = AddMesh(Mesh.Plane, new Vector3(0, 5.99f, 0), new Vector3(2, 1, 2), Color.White, new Vector3(0, 0, MathHelper.Pi));
        ECS.AddComponent<SurfaceEmission>(emitter) = new SurfaceEmission { Radiance = new Vector3(6) };

        Inspector.CreateSceneWindow("cornellBox", "Cornell Box", Visible: false);
        Inspector.AddLabel("cornellBox", "rendering", "Flat / raster / diffuse path tracing");
        Inspector.AddLabel("cornellBox", "camera", "Arrow keys: orbit / zoom");
        Inspector.AddLabel("cornellBox", "buffers", "Tab: available buffers | R: renderer");
        Inspector.AddLabel("cornellBox", "inspector", "F1: toggle framework inspector");

        UpdateCamera();
        SelectRenderer(InitialRenderMethod);
    }

    public Microsoft.Xna.Framework.Graphics.Texture2D GetRenderedOutput() => RenderMethod switch { 1 => Raster.GetOutput(), 2 => Denoiser.GetOutput(), _ => Flat.GetOutput() };

    public void SelectRenderer(int index)
    {
        if (index < 0 || index > 2) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == RenderMethod) return;
        AA.JitterEnabled = index != 2;
        AA.ResetHistory();
        Raster.Enabled = index == 1;
        Tracer.Enabled = index == 2;
        if (index != 1) Raster.Dispose();
        if (index != 2) Tracer.Dispose();
        bool needGeometry = index != 2;
        if (Geometry.Enabled != needGeometry)
        {
            Geometry.Enabled = needGeometry;
            if (needGeometry) Geometry.Initialize(); else Geometry.Dispose();
        }
        Tracer.ResetAccumulation();
        RenderMethod = index;
        Pipeline.SelectBuffer(0);
        Inspector.SetDropdownValue("geometry3d", "renderer", index);
    }

    private int AddMesh(Mesh mesh, Vector3 position, Vector3 scale, Color color, Vector3 rotation = default)
    {
        int entity = ECS.CreateEntity(position);
        ref var transform = ref ECS.GetComponent<Transform3D>(entity);
        transform.Scale = scale;
        transform.Rotation = rotation;
        ECS.AddComponent<Mesh3D>(entity) = new Mesh3D { Geometry = mesh, Color = color };
        return entity;
    }

    private void UpdateCamera()
    {
        ref var camera = ref ECS.GetComponent<Camera3D>(Camera);
        camera.Target = new Vector3(0, 2.8f, 0);
        camera.Position = camera.Target + new Vector3(MathF.Sin(Angle) * Distance, 0.6f, MathF.Cos(Angle) * Distance);
    }

    public override void Update()
    {
        if (!Renderer.IsActive) return;
        var keyboard = Keyboard.GetState();
        if (Inspector.IsEditingText) { PreviousKeyboard = keyboard; return; }
        float previousAngle = Angle, previousDistance = Distance;
        if (keyboard.IsKeyDown(Keys.Left)) Angle = MathHelper.Max(-0.22f, Angle - DeltaTime * 0.3f);
        if (keyboard.IsKeyDown(Keys.Right)) Angle = MathHelper.Min(0.22f, Angle + DeltaTime * 0.3f);
        if (keyboard.IsKeyDown(Keys.Up)) Distance = MathHelper.Max(9, Distance - DeltaTime * 5);
        if (keyboard.IsKeyDown(Keys.Down)) Distance = MathHelper.Min(16, Distance + DeltaTime * 5);
        if (keyboard.IsKeyDown(Keys.Tab) && PreviousKeyboard.IsKeyUp(Keys.Tab))
        {
            Pipeline.SelectNextBuffer();
        }
        if (keyboard.IsKeyDown(Keys.R) && PreviousKeyboard.IsKeyUp(Keys.R)) SelectRenderer((RenderMethod + 1) % 3);
        PreviousKeyboard = keyboard;
        if ((Angle != previousAngle || Distance != previousDistance) && Meshes.CameraEntity == Camera) UpdateCamera();
    }
}
