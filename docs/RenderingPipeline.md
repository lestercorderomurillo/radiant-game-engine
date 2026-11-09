# Image quality and upscaling

Radiant's image stages form one chain that every pipeline can share:

`image → tone mapping → antialiasing → upscaling → buffer inspector → screen`

UI and gizmos are drawn afterwards, so they always stay sharp.

## Upscaling modes

- **None** renders at full resolution with no upscaling.
- **Native** keeps the selected upscaler but passes the image through untouched, handy for A/B comparisons.
- **Performance** and **Balanced** render at 25% and 50% of each dimension, then reconstruct.

## Add upscaling to your pipeline

`Upscaling` hosts the UDR upscalers behind one switch. Place it after antialiasing and present its output.

The following example shows the end of a pipeline with selectable upscaling:

```csharp
var aa = ECS.AddSystem(new AntiAliasing { Input = color.GetOutput, After = [color] });
var upscaler = ECS.AddSystem(new Upscaling { Input = aa.GetOutput, After = [aa] });
ECS.AddSystem(new RenderPresentation { Input = upscaler.GetOutput, After = [upscaler] });

upscaler.Select(UpscalingMode.UDR4);   // Or UpscalingMode.None to turn it off
```

So the producer renders fewer pixels, let the upscaler pick its resolution each frame:

```csharp
geometry.Resolution = upscaler.ResolveViewSize(geometry.Resolution);
```

Pass the upscaler to `AntiAliasingSettings.Upscaler` and players get an **Image quality** window (F1 → View) to
choose the upscaler, its quality, antialiasing and to reset history.

## Which upscaler can I use?

- With [2D global illumination](HRC.md), set `HrcInput = true` and every upscaler is available. UDR4 then uses
  motion and history for the best quality.
- For any other image (flat or lit shapes, path tracing, 3D), UDR4 runs as a spatial upscaler: sharp and
  alpha-preserving, without motion history.
