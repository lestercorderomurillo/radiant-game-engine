using System;
using com.radiant.engine.bundle;
using com.radiant.engine.input;

namespace com.radiant.engine.core;

/// <summary>Cornell-box demo for comparing flat, raster, path-traced and radiance-cascade rendering.</summary>
public class CornellBoxScene : Scene
{
    public int InitialRenderMethod { get; init; }
    public DenoiserMode InitialDenoiser { get; init; }
    public PTGIBackend InitialTraceBackend { get; init; } = PTGIBackend.Auto;

    public MeshGeometry3D Meshes { get; private set; }
    public Geometry3D Geometry { get; private set; }
    public RasterRenderer Raster { get; private set; }
    public PTGI Tracer { get; private set; }
    public RCGI Cascades { get; private set; }
    public Denoiser Denoiser { get; private set; }
    public LightingReconstructor Lighting { get; private set; }
    public Upscaler Upscaler { get; private set; }
    public AntiAliaser AA { get; private set; }
    public RasterSampler Sampling { get; private set; }
    public ToneMapper ToneMap { get; private set; }
    public RenderInspector Pipeline { get; private set; }
    public int RenderMethod { get; private set; }

    private FlatRenderer Flat;
    private int Camera;
    private float Angle;
    private float Distance = 11;
    private bool CascadeDefaultsApplied;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        ECS.AddSystemCollection(SystemCollections.Debugging);

        Sampling = ECS.AddSystem<RasterSampler>();
        Meshes = ECS.AddSystem(new MeshGeometry3D { Sampling = Sampling });
        Geometry = ECS.AddSystem(new Geometry3D { Channels = GeometryChannels.All, Source = Meshes, After = [Meshes] });

        Flat = ECS.AddSystem(new FlatRenderer { Input = Geometry.GetOutput, After = [Geometry] });
        Raster = ECS.AddSystem(new RasterRenderer { Geometry = Geometry, Input = Geometry.GetOutput, After = [Geometry] }, false);
        Tracer = ECS.AddSystem(new PTGI { Backend = InitialTraceBackend, SeparateLighting = true, Geometry = Meshes, Channels = TraceChannels.All, ViewSize = () => Meshes.ViewSize, After = [Meshes] }, false);
        Denoiser = ECS.AddSystem(new Denoiser { TraceSource = Tracer, Mode = InitialDenoiser, Input = () => Tracer.Enabled ? Tracer.GetOutput() : null, Albedo = () => Tracer.AlbedoTexture, Normals = () => Tracer.NormalTexture, Depth = () => Tracer.DepthTexture, Emission = () => Tracer.EmissionTexture, SingleSample = () => Tracer.SampleTexture, Camera = () => Tracer.Geometry.HasCamera ? Tracer.Geometry.Camera : null, Revision = () => Tracer.RadianceRevision, After = [Tracer] }, enabled: true);
        Lighting = ECS.AddSystem(new LightingReconstructor { Source = Tracer, Input = Denoiser.GetOutput, After = [Tracer, Denoiser] });
        Cascades = ECS.AddSystem(new RCGI { Geometry = Meshes, GridSize = 32, BaseDirectionRows = 8, ViewSize = () => Meshes.ViewSize, After = [Meshes] }, false);

        ToneMap = ECS.AddSystem(new ToneMapper { Input = GetRenderedOutput, Operator = ToneMapOperator.None, After = [Flat, Raster, Tracer, Denoiser, Lighting, Cascades] });
        AA = ECS.AddSystem(new AntiAliaser { Input = ToneMap.GetOutput, Mode = PostAAMode.None, OutputSize = () => Meshes.ViewSize, Camera = () => Meshes.Camera, Depth = () => RenderMethod switch { 2 => Lighting.DepthTexture ?? Tracer.DepthTexture, 3 => Cascades.DepthTexture, _ => Geometry.DepthTexture }, GeometryRevision = () => Meshes.Revision, After = [ToneMap] });
        AA.SetSampling(Sampling);
        Upscaler = ECS.AddSystem(new Upscaler { Input = AA.GetOutput, After = [AA] });

        ECS.AddSystem(new ToneMapperSettings { Id = "geometry3dColor", Source = ToneMap });
        ECS.AddSystem(new AntiAliaserSettings { Id = "geometry3dAA", Source = AA, Sampling = Sampling, Upscaler = Upscaler, Title = "Image quality" });
        ECS.AddSystem(new PTGISettings { Id = "pathTracing", Source = Tracer, Denoiser = Denoiser, Changed = AA.ResetHistory });
        ECS.AddSystem(new RCGISettings { Id = "radianceCascades", Source = Cascades, Changed = AA.ResetHistory });

        Pipeline = ECS.AddSystem(new RenderInspector { Id = "geometry3d", Title = "3D Pipeline", Input = Upscaler.GetOutput, After = [Upscaler] });

        Pipeline.AddSource("Geometry", Geometry);
        Pipeline.AddSource("Raster", Raster);
        Pipeline.AddSource("Path tracing", Tracer);
        Pipeline.AddSource("Denoising", Denoiser);
        Pipeline.AddSource("Path lighting", Lighting);
        Pipeline.AddSource("Radiance cascades", Cascades);
        Pipeline.AddSource("Reconstruction", Upscaler);
        Pipeline.AddSource("Tone mapping", ToneMap);
        Pipeline.AddSource("AA", AA);

        ECS.AddSystem(new RenderPresenter { Input = Pipeline.GetOutput, ClearColor = new Color(18, 22, 30), Blend = BlendState.AlphaBlend, After = [Pipeline] });
    }

    /// <inheritdoc/>
    public override void SetupScene()
    {
        Camera = ECS.CreateEntity();
        ECS.AddComponent<Camera3D>(Camera) = new Camera3D { FarPlane = 40 };
        Meshes.CameraEntity = Camera;

        Inspector.AddDropdown("geometry3d", "renderer", "Renderer", new[] { "Flat", "Raster", "Path tracing", "Radiance cascades" }, 0, SelectRenderer);
        Inspector.AddDropdown("geometry3d", "fpsCap", "FPS Limit", runtime.GameLoop.FpsOptionNames, 2, index => Renderer.GameLoop.SetTargetFps(runtime.GameLoop.FpsOptions[index]));
        Inspector.AddToggle("geometry3d", "throttleUnfocused", "Limit FPS when unfocused", Renderer.GameLoop.ThrottleUnfocused, enabled => Renderer.GameLoop.ThrottleUnfocused = enabled);

        var white = new Color(210, 210, 200);
        var roomSize = new Vector3(6, 1, 6);

        // Open front (+Z); room surfaces face inward for future material/lighting passes.
        AddMesh(Mesh.Plane, Vector3.Zero, roomSize, white);
        AddMesh(Mesh.Plane, new Vector3(0, 6, 0), roomSize, white, new Vector3(0, 0, global::System.MathF.PI));
        AddMesh(Mesh.Plane, new Vector3(0, 3, -3), roomSize, white, new Vector3((global::System.MathF.PI / 2), 0, 0));
        AddMesh(Mesh.Plane, new Vector3(-3, 3, 0), roomSize, new Color(165, 32, 28), new Vector3(0, 0, -(global::System.MathF.PI / 2)));
        AddMesh(Mesh.Plane, new Vector3(3, 3, 0), roomSize, new Color(35, 135, 48), new Vector3(0, 0, (global::System.MathF.PI / 2)));

        AddMesh(Mesh.Cube, new Vector3(-1.1f, 0.9f, 1), new Vector3(1.7f, 1.8f, 1.7f), new Color(235, 235, 225), new Vector3(0, -0.3f, 0));
        AddMesh(Mesh.Cube, new Vector3(0.95f, 1.6f, -0.9f), new Vector3(1.8f, 3.2f, 1.8f), new Color(235, 235, 225), new Vector3(0, 0.3f, 0));

        int emitter = AddMesh(Mesh.Plane, new Vector3(0, 5.99f, 0), new Vector3(2, 1, 2), Color.White, new Vector3(0, 0, global::System.MathF.PI));
        ECS.AddComponent<SurfaceEmission>(emitter) = new SurfaceEmission { Radiance = new Vector3(6) };

        Inspector.CreateSceneWindow("cornellBox", "Cornell Box", Visible: false);
        Inspector.AddLabel("cornellBox", "rendering", "Flat / raster / path tracing / radiance cascades");
        Inspector.AddLabel("cornellBox", "camera", "Arrow keys: orbit / zoom");
        Inspector.AddLabel("cornellBox", "buffers", "Tab: available buffers | R: renderer");
        Inspector.AddLabel("cornellBox", "inspector", "F1: toggle framework inspector");

        UpdateCamera();
        SelectRenderer(InitialRenderMethod);
    }

    /// <summary>Returns the image from the currently selected flat, raster, path-traced or radiance-cascade renderer.</summary>
    public Texture2D GetRenderedOutput() => RenderMethod switch { 1 => Raster.GetOutput(), 2 => Lighting.GetOutput(), 3 => Cascades.GetOutput(), _ => Flat.GetOutput() };

    /// <summary>Activates the selected renderer and resets dependent image history and inspection.</summary>
    public void SelectRenderer(int index)
    {
        if (index < 0 || index > 3) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == RenderMethod) return;

        if (index == 3 && !CascadeDefaultsApplied)
        {
            UDRQuality.Set(1);
            Upscaler.Select(UpscalerMode.UDR4);
            CascadeDefaultsApplied = true;
        }

        AA.JitterEnabled = index != 2;
        AA.ResetHistory();
        Upscaler.ResetHistory();

        Raster.Enabled = index == 1;
        Tracer.Enabled = index == 2;
        Cascades.Enabled = index == 3;

        if (index != 1) Raster.Dispose();
        if (index != 2) Tracer.Dispose();
        if (index != 3) Cascades.Dispose();

        bool needGeometry = index < 2;

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

    /// <summary>Creates a mesh entity with the supplied transform and color.</summary>
    private int AddMesh(Mesh mesh, Vector3 position, Vector3 scale, Color color, Vector3 rotation = default)
    {
        int entity = ECS.CreateEntity(position);

        ref var transform = ref ECS.GetComponent<Transform3D>(entity);
        transform.Scale = scale;
        transform.Rotation = rotation;

        ECS.AddComponent<Mesh3D>(entity) = new Mesh3D { Geometry = mesh, Color = color };

        return entity;
    }

    /// <summary>Applies the scene's orbit angle and distance to its camera.</summary>
    private void UpdateCamera()
    {
        ref var camera = ref ECS.GetComponent<Camera3D>(Camera);
        camera.Target = new Vector3(0, 2.8f, 0);
        camera.Position = camera.Target + new Vector3(MathF.Sin(Angle) * Distance, 0.6f, MathF.Cos(Angle) * Distance);
    }

    /// <inheritdoc/>
    public override void Update()
    {
        Meshes.Resolution = Upscaler.ResolveViewSize(Meshes.Resolution);

        if (!Renderer.IsActive) return;

        var keyboard = Controls.Keyboard;

        if (Inspector.IsEditingText) return;

        float previousAngle = Angle, previousDistance = Distance;

        if (keyboard.IsKeyDown(Key.Left)) Angle = global::System.Math.Max(-0.22f, Angle - DeltaTime * 0.3f);
        if (keyboard.IsKeyDown(Key.Right)) Angle = global::System.Math.Min(0.22f, Angle + DeltaTime * 0.3f);
        if (keyboard.IsKeyDown(Key.Up)) Distance = global::System.Math.Max(9, Distance - DeltaTime * 5);
        if (keyboard.IsKeyDown(Key.Down)) Distance = global::System.Math.Min(16, Distance + DeltaTime * 5);
        if (Controls.WasPressed(Key.Tab)) Pipeline.SelectNextBuffer();
        if (Controls.WasPressed(Key.R)) SelectRenderer((RenderMethod + 1) % 4);


        if ((Angle != previousAngle || Distance != previousDistance) && Meshes.CameraEntity == Camera) UpdateCamera();
    }
}
