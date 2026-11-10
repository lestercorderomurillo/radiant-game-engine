# Neural denoising

Path tracing is noisy at low sample counts. `Denoising` cleans the image before tone mapping, without touching the
tracer's own accumulation, so lighting stays smooth while the camera moves.

## Pick a mode

| Mode | Best for |
| --- | --- |
| Joint bilateral | A fast, light cleanup |
| À-trous | Strong cleanup of still or slow scenes |
| SVGF (temporal) | Moving cameras |
| OIDN (neural) | Highest-quality previews |
| NRD REBLUR / RELAX | Hardware ray tracing on Vulkan |

## Connect the guide buffers

Denoisers separate noise from detail using albedo, normals, depth and emission from the tracer.

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

## Neural backends

**OIDN** runs Intel Open Image Denoise. Install it once with `python3 eng/install-oidn.py`, rebuild, and pick
Fast, Balanced or High quality on CPU or GPU under **Neural denoising settings**. It copies images through the
CPU, so use it for stills and previews; prefer SVGF while the camera moves.

**NRD** works with hardware ray tracing. Build the runtime with `python3 eng/build-vulkan-rt.py --nrd`, set
`denoiser.TraceSource = tracer`, and select it with `denoiser.TrySelectBackend("nrd.reblur", out var reason)`.

## Plug in your own

The following example shows how to register and select a custom backend:

```csharp
denoiser.RegisterBackend("studio.neural", () => new StudioDenoiser());
if (!denoiser.TrySelectBackend("studio.neural", out string reason)) Console.WriteLine(reason);
```

## Continue

- [Global illumination](HRC.md): the path tracer that produces the noisy input.
- [Tone mapping](ToneMapping.md): the stage that follows denoising.
