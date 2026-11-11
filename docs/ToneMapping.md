# Tone mapping

Turn bright linear light into a display-ready image. `ToneMapper` is one stage in the image pipeline, so the
same operators serve 2D global illumination, raster 3D and path tracing.

## Choose an operator

- **Operators**: None, Reinhard, ACES, ACES Fitted, AgX, Filmic, Uchimura and PBR Neutral.
- **Exposure**: a brightness multiplier, applied even with operator None.
- **Transfer**: Linear (default), sRGB or Gamma 2.2 output.

The following example shows AgX tone mapping with an in-game color window:

```csharp
var color = ECS.AddSystem(new ToneMapper { Input = renderer.GetOutput, Operator = ToneMapOperator.AgX, After = [renderer] });
ECS.AddSystem(new ToneMapperSettings { Id = "mainColor", Source = color });
```

The **Color Management** window appears under F1 → View, with the operator, exposure and transfer controls.

## Place it in the pipeline

Tone map after lighting and denoising, and before anti-aliasing and upscaling, so edge filters and
reconstruction work on display-range colors:

`lighting → denoising → tone mapping → anti-aliasing → upscaling → presentation`

For 2D global illumination, `HRCToneMapper` gives you the same operators in the place UDR upscalers expect.

## Continue

- [Image quality](RenderingPipeline.md): choose the stage for each visual problem.
- [Rendering pipelines](Rendering.md): full producer and stage examples.
