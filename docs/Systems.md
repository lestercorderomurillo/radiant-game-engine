# Systems

A [`System`](../src/com.radiant.engine/core/System.cs) holds behavior that runs every frame. A
[`Scene`](../src/com.radiant.engine/core/Scene.cs) bundles systems and entities into a level, a menu or any other screen.

## Write a system

Override only the callbacks you need.

The following example shows a system that spins every rectangle and stops while the game is paused:

```csharp
[Pausable]
public class Spinner : System
{
    public float Speed = 2f;

    public override void FixedUpdate()
    {
        float step = Speed * Time.DeltaSeconds;
        Scene.ECS.Query<Transform2D, Rectangle2D>((int thread, int entity, ref Transform2D transform, ref Rectangle2D rectangle) =>
            transform.Rotation += step);
    }
}
```

Register it in your scene with `ECS.AddSystem<Spinner>()`.

## Reuse a system collection

Register the common debugging tools once in `SetupECS`:

```csharp
ECS.AddSystemCollection(SystemCollections.Debugging);
```

Import `com.radiant.engine.bundle` for the built-in collection. It adds `Inspector`, `PerformanceMonitor`
and `GizmosRenderer`, reusing any instances already registered. Inspector includes entity, component and
system editing, logs and shared camera controls. It opens in Panels mode with Show All enabled.
Camera Controls discovers entity cameras in 2D/3D, fitted 2D views and renderer-owned tile viewports.
Enable **Navigate camera**, then hold the right mouse button and use WASD (Q/E moves vertically in 3D).

Collections contain registration recipes, never shared system instances. Calling the same collection twice
on one ECS applies it once; each scene receives its own systems. Define your own recipe with:

```csharp
public static readonly SystemCollection Movement = new("Movement", ecs =>
{
    ecs.AddSystem<Spinner>();
});
```

Register collections before ECS initialization. To customize the debug workspace, configure its Inspector
in `SetupECS` after registering the collection:

```csharp
var inspector = ECS.GetSystem<Inspector>();
inspector.StartupWorkspaceMode = WorkspaceMode.Windows; // Default: Panels
inspector.ShowAllOnStartup = false;                     // Default: true
inspector.Visible = false;                             // Default: true; F1 toggles it
```

## Choose the right callback

| Callback | Use it for |
| --- | --- |
| `Initialize` | Creating render targets, windows and other resources, once |
| `Update` | Key presses, typed text and anything tied to the frame rate |
| `FixedUpdate` | Movement, physics and simulation, 64 times per second |
| `Render` | Producing images other systems read, such as lighting |
| `LateRender` | Overlays and UI drawn on top of the scene |
| `OnResize` | Recreating targets that depend on the window size |
| `Dispose` | Releasing what `Initialize` created |

Every system can use `Time.DeltaSeconds`, `Controls` (input), `Renderer` and `Scene`.

## Order systems

Declare `[RunAfter(...)]` and `[RunBefore(...)]` on a system, or set `After` between instances, and the engine
sorts the frame for you. `LateRender` output is additionally stacked by `RenderLayer`. See
[Dependency resolution](DependencyResolution.md).

## Pause, group and switch

`[Pausable]`, `Enabled`, `[SystemTag]`, `[CoreSystem]` and `SystemGroup` control which systems run and how the
inspector shows them. See [System control](SystemControl.md).

## Build a scene

A scene registers systems in `SetupECS` and creates content in `SetupScene`; the engine initializes the systems
between the two. See [Scenes](Scenes.md).

Use `ECS.GetSystem<T>()` to reach a registered system, for example `ECS.GetSystem<GizmosRenderer>()`.

## API names

Systems that perform a job use a concrete role: `RasterRenderer`, `Denoiser`, `Upscaler`, `ToneMapper`,
`RasterSampler`, `AntiAliaser`, `LightingReconstructor` and `RenderPresenter`. Geometry sources keep their
descriptive nouns, such as `ShapeGeometry2D` and `MeshGeometry3D`. Tools keep names such as `Inspector`
and `PerformanceMonitor`. A component like `Movement2D` describes data; `Movement2DSystem` remains
distinct from that component.

Named techniques retain their acronyms: `HRCGI` is Holographic Radiance Cascades Global Illumination,
`PTGI` is Path-Traced Global Illumination, and `UDR1`–`UDR4` are Universal Detail Reconstruction algorithms.
The experimental dense 3D cascade system is `RCGI` (Radiance Cascades Global Illumination).
`SRCGI` means Split Radiance Cascades Global Illumination and is reserved for that unimplemented algorithm.

Options describe what they select: `DenoiserMode`, `UpscalerMode`, `PTGIBackend` and `ToneMapOperator`.
Inspector controls use their system's name plus `Settings`, such as `PTGISettings` and `ToneMapperSettings`.
Operation names such as rendering, denoising and tone mapping still describe concepts in guides and UI.

`HRCToneMapper` processes HRC/UDR images during Update. Render-phase pipelines compose
`ToneMapper` and `ToneMapperSettings`.
