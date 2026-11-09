# Lighting

Choose how light interacts with your scene, then connect the result to the same image pipeline. Radiant provides lighting techniques for both 2D and 3D; the technique determines which geometry, materials and buffers it needs.

## Choose a technique

| Technique | Scene | Use it for |
| --- | --- | --- |
| **Holographic Radiance Cascades** (`HRCGI`) | 2D shapes | Emissive lights, soft shadows and colored transmission |
| **Raster lighting** (`RasterRenderer`) | 2D or 3D raster geometry | Directional diffuse shading with an ambient term |
| **Path tracing** (`PathTracer`) | 2D or 3D trace geometry | Progressive diffuse global illumination |
| **Unlit rendering** (`FlatRenderer`) | 2D or 3D images | Preserve authored colors without a lighting pass |

Lighting technique and execution backend are separate choices. Vulkan ray queries accelerate path tracing; they do not define the lighting model. Toon lighting is not a built-in technique yet. Custom shader passes can provide other looks without changing the rest of the pipeline.

## Holographic Radiance Cascades

HRC is the current lighting technique for emissive 2D shapes. `Geometry` supplies the scene buffers and `HRCGI` computes the illumination. Moving or recoloring a shape updates the light on the next rendered frame.

### Set up a 2D scene

Register both systems before calling the base setup:

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

These are **lighting quality** settings. They are separate from the resolution presets used by [Upscaling](UDR4.md).

`HRCGI.GetOutput()` supplies linear light. Connect it to `ColorManagement` before display, then add reconstruction when rendering below display resolution:

```csharp
var lighting = ECS.AddSystem<HRCGI>();
var color = ECS.AddSystem<ColorManagement>();
var upscaler = ECS.AddSystem<UDR4>();

// Connecting providers is fine inside SetupECS; the engine initializes systems afterwards.
color.SetInputSource(lighting.GetOutput);
upscaler.SetInputSource(color.GetOutput);
```

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
- [Upscaling](UDR4.md) — compare UDR1–UDR4 and their input requirements.
- [3D scenes](3D.md) — create meshes, cameras and surface buffers.
