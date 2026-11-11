# Anti-aliasing

Smooth jagged edges by taking more samples while drawing, by filtering the finished image, or both. Each project
picks the combination that fits its budget.

## Pick a method

| Geometry (`RasterSampler.Mode`) | Post (`AntiAliaser.Mode`) |
| --- | --- |
| MSAA 2x / 4x / 8x: hardware multisampling | FXAA: fast edge smoothing |
| SSAA 2x / 3x: render bigger, then average | SMAA: sharper, high-quality edges |
| | TAA: smooth, stable edges using previous frames |

Geometry methods cost GPU time proportional to the sample count; post methods cost a fixed full-screen pass.
TAA needs a camera, depth and motion information so it can reproject the previous frame.

## Combine geometry and post

The following example shows MSAA 4x combined with TAA:

```csharp
var sampling = ECS.AddSystem(new RasterSampler { Mode = RasterAAMode.MSAA4x });
var meshes = ECS.AddSystem(new MeshGeometry3D { Sampling = sampling });
var geometry = ECS.AddSystem(new Geometry3D { Source = meshes, Channels = GeometryChannels.All, After = [meshes] });
var aa = ECS.AddSystem(new AntiAliaser
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
ECS.AddSystem(new AntiAliaserSettings { Id = "mainAA", Source = aa, Sampling = sampling });
```

`AntiAliaserSettings` adds the **Image quality** window under F1 → View, so the method can be switched while
the game runs. `RasterSampler.ActualSamples` reports what the GPU really used; some GPUs give 4x when you ask
for 8x.

## Use TAA in 2D

Set `ShapeGeometry2D.CaptureDepth = true` so the 2D producer writes the depth TAA needs.

## Continue

- [Image quality](RenderingPipeline.md): balance anti-aliasing against lighting quality and upscaling.
- [UDR](UDR4.md): reconstruction that also stabilises edges.
