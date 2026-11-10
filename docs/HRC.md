# Global illumination

Choose how light interacts with your scene, then connect the result to the same image pipeline. Radiant provides lighting techniques for both 2D and 3D; the technique determines which geometry, materials and buffers it needs.

## Choose a technique

| Technique | Scene | Use it for |
| --- | --- | --- |
| **Holographic Radiance Cascades** (`HRCGI`) | 2D shapes | Emissive lights, soft shadows and colored transmission |
| **3D Radiance Cascades** (`RadianceCascades`, experimental) | 3D trace geometry | Deterministic diffuse lighting with world-space probes and optional bounce feedback |
| **Raster lighting** (`RasterRenderer`) | 2D or 3D raster geometry | Directional diffuse shading with an ambient term |
| **Path tracing** (`PathTracer`) | 2D or 3D trace geometry | Progressive diffuse global illumination |
| **Unlit rendering** (`FlatRenderer`) | 2D or 3D images | Preserve authored colors without a lighting pass |

Lighting technique and execution backend are separate choices. Vulkan ray queries accelerate path tracing; they do not define the lighting model. Toon lighting is not a built-in technique yet. Custom shader passes can provide other looks without changing the rest of the pipeline.

## Holographic Radiance Cascades

HRC is the current lighting technique for emissive 2D shapes. `Geometry` supplies the scene buffers and `HRCGI` computes the illumination. Moving or recoloring a shape updates the light on the next rendered frame.

### Set up a 2D scene

The following example shows how to register lighting and create a light and wall:

```csharp
public override void SetupECS()
{
    ECS.AddSystem<Geometry>();
    ECS.AddSystem<HRCGI>();
}

public override void SetupScene()
{
    Renderer.FitWorld(Renderer.VirtualSize / 2, Renderer.VirtualSize);
    LightFactory.CreateLight(ECS, new Vector2(1500, 1080), 60f,
        Color.White, Color.White);

    int wall = ECS.CreateEntity(new Vector2(1900, 700));
    ECS.AddComponent<Rectangle2D>(wall) = new Rectangle2D(new Vector2(60, 700));
    ECS.AddComponent<Material>(wall).Albedo = Color.Black;
}
```

### Shape the light

HRC interprets `Material` as emission and transmission. These settings are specific to this technique; they are not a universal material model for every renderer.

| Result | Material setting |
| --- | --- |
| Emissive light | Set `Emissive`; alpha controls brightness |
| Opaque blocker | Set `Albedo = Color.Black` |
| Colored transmission | Set `Albedo` to a color with partial alpha |
| No effect on light | Keep white `Albedo` and black `Emissive` |

For example, `new Color(60, 255, 60, 160)` in `Albedo` creates green transmission. Use `Renderer.FitWorld` to keep the world framing consistent as the window changes shape.

### Adjust quality

Open **F1 → View → Holographic Radiance Cascades**. Performance, Balanced, Ultra and Native change probe spacing; Ultra is the default. Wider spacing reduces lighting detail.

These are **lighting quality** settings. They are separate from the resolution presets used by [UDR](UDR4.md).

`HRCGI.GetOutput()` supplies linear light. Connect it to `ColorManagement` before display, then add reconstruction when rendering below display resolution:

```csharp
var lighting = ECS.AddSystem<HRCGI>();
var color = ECS.AddSystem<ColorManagement>();
var upscaler = ECS.AddSystem<UDR4>();

// Connecting providers is fine inside SetupECS; the engine initializes systems afterwards.
color.SetInputSource(lighting.GetOutput);
upscaler.SetInputSource(color.GetOutput);
```

## 3D Radiance Cascades

`RadianceCascades` lights diffuse meshes through a cube of world-space probes. It
uses the same `ITraceGeometry` and `SurfaceEmission` inputs as path tracing.
Primary surfaces are shaded at the requested view size; probe density controls
lighting detail independently. No stochastic sampling or denoiser is required,
but coarse probes and angular sampling can still cause visible artifacts.

The following example shows how to connect the experimental stage to a 3D mesh
adapter, tone mapping and presentation:

```csharp
var meshes = ECS.AddSystem<MeshGeometry3D>();
var lighting = ECS.AddSystem(new RadianceCascades
{
    Geometry = meshes,
    After = [meshes],
    GridSize = 16,
    BaseDirectionRows = 4,
    MultiBounce = true
});
var color = ECS.AddSystem(new ToneMapping
{
    Input = lighting.GetOutput,
    After = [lighting]
});
ECS.AddSystem(new RenderPresentation
{
    Input = color.GetOutput,
    After = [color]
});
```

Supply meshes and an active camera as described in [2D and 3D scenes](3D.md).
The Cornell demo offers **Radiance cascades** in its renderer selector, with
controls under **F1 → View → Radiance cascades**. It starts with a 16³ probe grid
and **UDR4 → Balanced**, rendering at 50% of each display dimension and
reconstructing to the full window size. Change reconstruction under
**F1 → View → Image quality**; later renderer switches preserve that choice.
UDR4 uses spatial reconstruction for this 3D input. Other scenes can register
`RadianceCascadesSettings` with `Source = lighting` alongside their `Inspector`.

- `GridSize` accepts 8, 16, 32 or 64 probes per axis; the standalone stage defaults to 32,
  while Cornell selects 16 for interactive use. Doubling it increases base probe count eightfold.
- `BaseDirectionRows` accepts 2, 4 or 8, corresponding to 8, 32 or 128 base
  directions. A 64³ grid supports at most 4 rows. Inspector controls adjust the
  other setting to keep the combination within the atlas limit.
- `LeakFix` connects each upper probe separately. It reduces interpolation leaks
  at a substantial tracing cost; disabling it can introduce light through walls.
- `MultiBounce` feeds the previous frame's irradiance back into surface hits.
  Disabling it leaves emission, environment and one diffuse reflection. Scene,
  lighting and layout changes restart feedback; `ResetBounces()` restarts it manually.
- `MaxBounceUpdates` defaults to 32. After that many updates, unchanged lighting
  reuses the world-space probe field, including while the camera moves. Single-bounce
  lighting is reused after its first update. Set the limit to 0 for continuous
  updates, or increase it for very reflective materials that need more iterations.
  This is an iteration budget, not a measured convergence threshold.
- `Volume` can specify a cube as `(Center, Size)`; otherwise it fits geometry bounds.
  `NormalBias` offsets lighting lookups in probe-spacing units.

The output is linear HDR; `DepthTexture` supplies linear depth for image stages.
This implementation uses GPU shader BVH traversal, without Vulkan ray-query
acceleration. It is intended for bounded-scene experiments: large volumes, thin
walls, small emitters and high probe counts need careful evaluation. It supports
opaque diffuse materials only, and multiple bounces take successive frames to settle.

## Raster lighting

Use `RasterRenderer` for directional diffuse shading. It accepts an `IRasterGeometry` producer, including the 2D and 3D raster adapters. With geometry supplied, it shades covered samples before MSAA resolves the image.

```csharp
var lighting = ECS.AddSystem(new RasterRenderer
{
    Geometry = geometry,
    Input = geometry.GetOutput,
    After = [geometry],
    LightColor = Vector3.One,
    Ambient = 0.12f
});
```

Here `geometry` is your existing raster geometry system. `LightDirection`, `LightColor` and `Ambient` control the light. This pass does not provide HRC transmission or path-traced indirect light.

## Path tracing

Use `PathTracer` with an `ITraceGeometry` adapter to accumulate diffuse light transport in 2D or 3D. For a 3D scene, `MeshGeometry3D` supplies the triangles:

```csharp
var meshes = ECS.AddSystem<MeshGeometry3D>();
var lighting = ECS.AddSystem(new PathTracer
{
    Geometry = meshes,
    After = [meshes],
    MaxBounces = 4,
    ResolutionScale = 0.5f
});
var color = ECS.AddSystem(new ToneMapping
{
    Input = lighting.GetOutput,
    After = [lighting]
});
```

The image converges over successive samples. Use `SurfaceEmission` for emissive 3D surfaces, and insert `Denoising` before tone mapping when low sample counts are too noisy. The current material model is diffuse; glossy and transparent surfaces are not traced.

`PathTracingBackend.Auto` selects Vulkan ray queries when available and otherwise uses shader BVH traversal. `Software` still runs on the GPU; it does not mean CPU rendering. See [Rendering pipelines](Rendering.md#hardware-ray-tracing) for the optional Vulkan runtime setup.

## Build the final image

Lighting produces an image; it does not replace the rest of the rendering pipeline. Choose the appropriate color stage, antialiasing, reconstruction and presentation for that output.

- [Rendering pipelines](Rendering.md) — connect geometry, lighting and image stages.
- [Image quality](RenderingPipeline.md) — order denoising, tone mapping and antialiasing.
- [UDR](UDR4.md) — compare UDR1–UDR4 and their input requirements.
- [2D and 3D scenes](3D.md) — create meshes, cameras and surface buffers.
