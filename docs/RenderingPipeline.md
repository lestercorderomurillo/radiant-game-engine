# Image quality

Control noise, jagged edges, brightness and resolution as separate stages. The same image pipeline can process 2D and 3D rendering, while each stage uses only the buffers it needs.

## Choose the stage for the problem

| Image problem | Stage | What to adjust |
| --- | --- | --- |
| Path-traced noise | `Denoising` | Backend, guide buffers and temporal settings |
| Exposure or highlights | `ToneMapping` or HRC's `ColorManagement` | Exposure and tone curve |
| Jagged geometry edges | `RasterSampling` and `AntiAliasing` | Geometry samples or image-space method |
| Rendering cost at high resolution | `Upscaling` | UDR algorithm and resolution preset |
| Need to inspect intermediate images | `RenderInspector` | Select a registered buffer |

Upscaling does not replace lighting, and antialiasing does not replace denoising. Start with the stage that addresses the visible problem.

## Order the pipeline

For a path-traced image, denoise before tone mapping. For raster rendering, shade geometry before resolving its samples. A typical image-stage chain is:

`lighting → denoising (if needed) → tone mapping → antialiasing → upscaling → presentation`

UI and gizmos are drawn afterwards at display resolution. Wire `Input` to the preceding stage's `GetOutput`, and use `After` to express execution order. See [Rendering pipelines](Rendering.md) for complete producer and stage examples.

## Balance resolution and detail

Three controls serve different purposes:

- **Lighting quality** changes the producer's work, such as HRC probe spacing or path-tracing bounce count.
- **Antialiasing** changes how edges are sampled or filtered.
- **Upscaling quality** changes input resolution before reconstruction.

The UDR algorithms are UDR1, UDR2, UDR3 and UDR4. Performance, Balanced and Native are their shared resolution presets. See [Upscaling](UDR4.md) for the compatibility table and setup.

## Inspect the result

Add `Inspector` and the relevant settings systems to expose controls under **F1 → View**. `AntiAliasingSettings.Upscaler` connects the selected `Upscaling` stage to the **Image quality** window.

Compare the same camera view at native resolution and at the intended render scale. Check silhouettes, fine detail, moving objects and changes in light. Use `RenderInspector` to distinguish a problem in lighting from one introduced by a later image stage.

## Continue

- [Lighting](HRC.md) — HRC, raster lighting and path tracing.
- [Upscaling](UDR4.md) — algorithms, quality presets and history.
- [Rendering pipelines](Rendering.md#denoise) — denoiser setup and guide buffers.
- [Inspector](Inspector.md) — controls and diagnostic overlays.
