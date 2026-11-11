# Rendering pipelines

Compose exactly the image you need from small, independent systems: produce an image (flat, lit or path traced),
then color-grade it, antialias it, denoise it, upscale it and present it. 2D and 3D use the same stages, and one
game can mix them.

Radiant is a general-purpose engine. A producer can render unlit content, evaluate lighting at runtime,
use prebaked GI supplied by your game, or implement a custom technique. Image stages consume the resulting
color image independently of that choice. Features that use depth, normals, geometry or motion require
compatible buffers for the same view; those capabilities are separate from the lighting method.

## Building blocks

| System | What it gives you |
| --- | --- |
| `ShapeGeometry2D` | Rectangles, circles and triangles as an image (with rotation, scale and layers) |
| `MeshGeometry3D` + `Geometry3D` | 3D meshes as an image, plus optional normals and depth |
| `FlatRenderer` | The unlit image, as is |
| `RasterRenderer` | Directional lighting with an adjustable light color and ambient term |
| `HRCGI` | Holographic Radiance Cascades Global Illumination for 2D emissive shapes |
| `PTGI` | Progressive path-traced global illumination |
| `RCGI` | Experimental diffuse 3D lighting with reusable world-space probes |
| `Denoiser` | Cleans up path-traced noise |
| `LightingReconstructor` | Reconstructs reduced-resolution PTGI lighting on full-resolution surfaces |
| `ToneMapper` | Exposure and filmic tone curves |
| `RasterSampler` + `AntiAliaser` | MSAA, SSAA, FXAA, SMAA and TAA |
| `Upscaler` | UDR upscalers |
| `RenderPresenter` | Draws a result to the screen or into a rectangle |
| `RenderInspector` | Lets you browse every buffer with Tab |

`RCGI` means Radiance Cascades Global Illumination. Its current implementation uses dense world-space
probes. `SRCGI` is reserved for Split Radiance Cascades Global Illumination, a different algorithm that is
not implemented here. `PTGI` means Path-Traced Global Illumination. See the
[API naming guide](Systems.md#api-names) for system, technique and option names.

Stages connect through `Input = previous.GetOutput` and run in order through `After = [previous]`. A disabled stage
passes its input straight through, and nothing allocates GPU memory until it's needed.

## Start simple

The following example shows the smallest 2D pipeline:

```csharp
var geometry = ECS.AddSystem<ShapeGeometry2D>();
ECS.AddSystem(new RenderPresenter { Input = geometry.GetOutput, After = [geometry] });
```

The following example shows the smallest 3D pipeline:

```csharp
var meshes = ECS.AddSystem<MeshGeometry3D>();
var geometry = ECS.AddSystem(new Geometry3D { Source = meshes, After = [meshes] });
var flat = ECS.AddSystem(new FlatRenderer { Input = geometry.GetOutput, After = [geometry] });
ECS.AddSystem(new RenderPresenter { Input = flat.GetOutput, After = [flat] });
```

## Add lighting

`RasterRenderer` lights each surface sample before antialiasing resolves it, which keeps edges clean.

The following example shows lit raster rendering:

```csharp
var raster = ECS.AddSystem(new RasterRenderer { Geometry = geometry, Input = geometry.GetOutput, After = [geometry] });
```

For emissive 2D shapes, shadows and colored transmission, see the HRC technique in [Global illumination](HRC.md#holographic-radiance-cascades).

## Light 3D meshes with radiance cascades

`RCGI` takes `MeshGeometry3D` directly as its trace geometry source.
Its output contains linear HDR lighting and emission; it also exposes linear
depth through `DepthTexture`. Raster geometry buffers and a denoiser are not
required for this path.

The following example shows how to connect the lighting stage to tone mapping:

```csharp
var cascades = ECS.AddSystem(new RCGI
{
    Geometry = meshes,
    ViewSize = () => meshes.ViewSize,
    GridSize = 16,
    After = [meshes]
});
var color = ECS.AddSystem(new ToneMapper
{
    Input = cascades.GetOutput,
    After = [cascades]
});
```

Here `meshes` is the scene's `MeshGeometry3D` system. Continue with antialiasing,
[UDR4 reconstruction](UDR4.md#connect-3d-radiance-cascades) and presentation. When
using TAA, supply `cascades.DepthTexture` to its depth provider and the mesh
adapter's camera to its camera provider.

The Cornell demo selects a 16³ probe grid and UDR4 Balanced when radiance cascades
are first activated. The standalone lighting stage defaults to 32³ probes; a new
`Upscaler` stage starts disabled until you select an algorithm. See
[3D Radiance Cascades](HRC.md#3d-radiance-cascades) for materials, bounce caching
and the limitations of this experimental technique.

## Path trace

`PTGI` traces light bouncing through the scene, in 2D or 3D, and refines the image every frame the camera
holds still.

The following example shows path tracing followed by tone mapping:

```csharp
var tracer = ECS.AddSystem(new PTGI { Geometry = meshes, After = [meshes] });
var color = ECS.AddSystem(new ToneMapper { Input = tracer.GetOutput, After = [tracer] });
```

- `ResolutionScale` traces fewer pixels for speed, independently of the rest of the pipeline.
- `MaxBounces` limits how far light travels.
- `Channels` adds noise-free albedo, normals, depth and emission buffers, which denoisers and TAA use:
  `tracer.Channels = TraceChannels.All`.
- Glowing 3D surfaces use a `SurfaceEmission` component.
- `LightingReconstructor`, placed after denoising, lets you lower the **Lighting resolution** while silhouettes, colors and
  emission stay at full resolution.

Surfaces are diffuse (matte); glossy and transparent materials aren't traced.

### Hardware ray tracing

On GPUs with Vulkan ray queries, the path tracer can run on dedicated ray-tracing hardware. Build the optional
runtime once, then build or run with it:

```sh
python3 eng/build-vulkan-rt.py
dotnet run --project MyGame -c Release -p:RadiantVulkanRT=true
```

`PTGI.Backend` chooses `Auto` (hardware when available), `Software` or `Vulkan`. The **Path tracing** window
shows which backend is active and its GPU time.

## Denoise

`Denoiser` cleans path-traced noise before tone mapping, with bilateral, à-trous, SVGF, OIDN and NRD backends.
See [Neural denoising](Denoising.md).

## Tone map and color grade

`ToneMapper` turns linear light into a display-ready image with Reinhard, ACES, AgX, Filmic and other operators.
See [Tone mapping](ToneMapping.md).

## Antialias

`RasterSampler` (MSAA, SSAA) and `AntiAliaser` (FXAA, SMAA, TAA) combine geometry and post anti-aliasing.
See [Anti-aliasing](AntiAliasing.md).

## Show several views

Each `RenderPresenter` has its own input, target and destination rectangle, so you can show a 2D map next to a
3D view, or two cameras side by side. Keep the references `AddSystem` returns and connect each view's stages to
each other with `After`.

## Inspect buffers

Add a `RenderInspector` to a view, then press **Tab** or use **F1** to step through every available buffer:
color, normals, depth, path-tracing guides, denoiser history and more. Raw buffers are shown without tone
mapping, exactly as the next stage sees them.
