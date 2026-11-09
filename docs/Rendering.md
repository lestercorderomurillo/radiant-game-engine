# Rendering pipelines

Compose exactly the image you need from small, independent systems: produce an image (flat, lit or path traced),
then color-grade it, antialias it, denoise it, upscale it and present it. 2D and 3D use the same stages, and one
game can mix them.

## Building blocks

| System | What it gives you |
| --- | --- |
| `ShapeGeometry2D` | Rectangles, circles and triangles as an image (with rotation, scale and layers) |
| `MeshGeometry3D` + `Geometry3D` | 3D meshes as an image, plus optional normals and depth |
| `FlatRenderer` | The unlit image, as is |
| `RasterRenderer` | Directional lighting with an adjustable light color and ambient term |
| `PathTracer` | Progressive path-traced global illumination |
| `Denoising` | Cleans up path-traced noise |
| `ToneMapping` | Exposure and filmic tone curves |
| `RasterSampling` + `AntiAliasing` | MSAA, SSAA, FXAA, SMAA and TAA |
| `Upscaling` | UDR upscalers |
| `RenderPresentation` | Draws a result to the screen or into a rectangle |
| `RenderInspector` | Lets you browse every buffer with Tab |

Stages connect through `Input = previous.GetOutput` and run in order through `After = [previous]`. A disabled stage
passes its input straight through, and nothing allocates GPU memory until it's needed.

## Start simple

The following example shows the smallest 2D pipeline:

```csharp
var geometry = ECS.AddSystem<ShapeGeometry2D>();
ECS.AddSystem(new RenderPresentation { Input = geometry.GetOutput, After = [geometry] });
```

The following example shows the smallest 3D pipeline:

```csharp
var meshes = ECS.AddSystem<MeshGeometry3D>();
var geometry = ECS.AddSystem(new Geometry3D { Source = meshes, After = [meshes] });
var flat = ECS.AddSystem(new FlatRenderer { Input = geometry.GetOutput, After = [geometry] });
ECS.AddSystem(new RenderPresentation { Input = flat.GetOutput, After = [flat] });
```

## Add lighting

`RasterRenderer` lights each surface sample before antialiasing resolves it, which keeps edges clean.

The following example shows lit raster rendering:

```csharp
var raster = ECS.AddSystem(new RasterRenderer { Geometry = geometry, Input = geometry.GetOutput, After = [geometry] });
```

For lit 2D scenes with glowing shapes, shadows and colored glass, use [2D global illumination](HRC.md) instead.

## Path trace

`PathTracer` traces light bouncing through the scene, in 2D or 3D, and refines the image every frame the camera
holds still.

The following example shows path tracing followed by tone mapping:

```csharp
var tracer = ECS.AddSystem(new PathTracer { Geometry = meshes, After = [meshes] });
var color = ECS.AddSystem(new ToneMapping { Input = tracer.GetOutput, After = [tracer] });
```

- `ResolutionScale` traces fewer pixels for speed, independently of the rest of the pipeline.
- `MaxBounces` limits how far light travels.
- `Channels` adds noise-free albedo, normals, depth and emission buffers, which denoisers and TAA use:
  `tracer.Channels = TraceChannels.All`.
- Glowing 3D surfaces use a `SurfaceEmission` component.
- `PathLighting`, placed after denoising, lets you lower the **Lighting resolution** while silhouettes, colors and
  emission stay at full resolution.

Surfaces are diffuse (matte); glossy and transparent materials aren't traced.

### Hardware ray tracing

On GPUs with Vulkan ray queries, the path tracer can run on dedicated ray-tracing hardware. Build the optional
runtime once, then build or run with it:

```sh
python3 eng/build-vulkan-rt.py
dotnet run --project tests/Radiant.3D -c Release -p:RadiantVulkanRT=true -- --path-backend=vulkan
```

`PathTracer.Backend` chooses `Auto` (hardware when available), `Software` or `Vulkan`. The **Path tracing** window
shows which backend is active and its GPU time.

## Denoise

Path tracing is noisy at low sample counts. `Denoising` cleans it up before tone mapping, without touching the
tracer's own accumulation.

| Mode | Best for |
| --- | --- |
| Joint bilateral | A fast, light cleanup |
| À-trous | Strong cleanup of still or slow scenes |
| SVGF (temporal) | Moving cameras |
| OIDN (neural) | Highest-quality previews |
| NRD REBLUR / RELAX | Hardware ray tracing on Vulkan |

The following example shows à-trous denoising with the guide buffers it needs:

```csharp
tracer.Channels = TraceChannels.All;

var denoiser = ECS.AddSystem(new Denoising
{
    Input = tracer.GetOutput,
    Mode = DenoiserMode.Atrous,
    Albedo = () => tracer.AlbedoTexture,
    Normals = () => tracer.NormalTexture,
    Depth = () => tracer.DepthTexture,
    Emission = () => tracer.EmissionTexture,
    After = [tracer]
});
var color = ECS.AddSystem(new ToneMapping { Input = denoiser.GetOutput, After = [denoiser] });
ECS.AddSystem(new PathTracingSettings { Id = "mainPath", Source = tracer, Denoiser = denoiser });
```

The following example shows the extra connections for temporal SVGF:

```csharp
denoiser.SingleSample = () => tracer.SampleTexture;
denoiser.Camera = () => tracer.Geometry.HasCamera ? tracer.Geometry.Camera : null;
denoiser.Revision = () => tracer.RadianceRevision;
denoiser.Mode = DenoiserMode.Svgf;
```

**OIDN** runs Intel Open Image Denoise. Install it once with `python3 eng/install-oidn.py`, rebuild, and pick
Fast, Balanced or High quality on CPU or GPU under **Neural denoising settings**. It copies images through the
CPU, so use it for stills and previews; prefer SVGF while the camera moves.

**NRD** works with hardware ray tracing. Build the runtime with `python3 eng/build-vulkan-rt.py --nrd`, set
`denoiser.TraceSource = tracer`, and select it with `denoiser.TrySelectBackend("nrd.reblur", out var reason)`.

Plug in your own denoiser by registering a backend:

```csharp
denoiser.RegisterBackend("studio.neural", () => new StudioDenoiser());
if (!denoiser.TrySelectBackend("studio.neural", out string reason)) Console.WriteLine(reason);
```

## Tone map and color grade

`ToneMapping` turns bright linear light into a display-ready image.

- **Operators**: None, Reinhard, ACES, ACES Fitted, AgX, Filmic, Uchimura and PBR Neutral.
- **Exposure**: a brightness multiplier, applied even with operator None.
- **Transfer**: Linear (default), sRGB or Gamma 2.2 output.

The following example shows AgX tone mapping with an in-game color window:

```csharp
var color = ECS.AddSystem(new ToneMapping { Input = renderer.GetOutput, Mapper = ToneMapper.AgX, After = [renderer] });
ECS.AddSystem(new ColorSettings { Id = "mainColor", Source = color });
```

The **Color Management** window appears under F1 → View. For 2D global illumination, `ColorManagement` gives you
the same operators in the place UDR upscalers expect.

## Antialias

Combine geometry antialiasing, which takes more samples while drawing, with post antialiasing, which filters the
finished image.

| Geometry (`RasterSampling.Mode`) | Post (`AntiAliasing.Mode`) |
| --- | --- |
| MSAA 2x / 4x / 8x: hardware multisampling | FXAA: fast edge smoothing |
| SSAA 2x / 3x: render bigger, then average | SMAA: sharper, high-quality edges |
| | TAA: smooth, stable edges using previous frames |

The following example shows MSAA 4x combined with TAA:

```csharp
var sampling = ECS.AddSystem(new RasterSampling { Mode = RasterAAMode.MSAA4x });
var meshes = ECS.AddSystem(new MeshGeometry3D { Sampling = sampling });
var geometry = ECS.AddSystem(new Geometry3D { Source = meshes, Channels = GeometryChannels.All, After = [meshes] });
var aa = ECS.AddSystem(new AntiAliasing
{
    Input = geometry.GetOutput,
    Mode = PostAAMode.TAA,
    Camera = () => meshes.Camera,
    Depth = () => geometry.DepthTexture,
    GeometryRevision = () => meshes.Revision,
    OutputSize = () => meshes.ViewSize,
    After = [geometry]
});
aa.SetSampling(sampling);
ECS.AddSystem(new AntiAliasingSettings { Id = "mainAA", Source = aa, Sampling = sampling });
```

`RasterSampling.ActualSamples` reports what the GPU really used; some GPUs give 4x when you ask for 8x. In 2D,
set `ShapeGeometry2D.CaptureDepth = true` to use TAA.

## Show several views

Each `RenderPresentation` has its own input, target and destination rectangle, so you can show a 2D map next to a
3D view, or two cameras side by side. Keep the references `AddSystem` returns and connect each view's stages to
each other with `After`.

## Inspect buffers

Add a `RenderInspector` to a view, then press **Tab** or use **F1** to step through every available buffer:
color, normals, depth, path-tracing guides, denoiser history and more. Raw buffers are shown without tone
mapping, exactly as the next stage sees them.

## Try it

```sh
dotnet run --project tests/Radiant.3D -c Release                    # Cornell Box: flat, raster, path tracing
dotnet run --project tests/Radiant.Scenes -c Release -- rendering2d  # The same pipelines in 2D
```
