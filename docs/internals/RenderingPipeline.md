# Shared reconstruction and image controls

The interactive test scenes use the same final-stage contract:

`producer → tone mapping → post AA → reconstruction → buffer inspector → presentation`

Geometry AA belongs to the producer's `RasterSampling`. Reconstruction belongs to `Upscaling`; presentation belongs to `RenderPresentation`. A scene chooses its content and producer; it does not need its own UDR switching implementation. UI and gizmos remain outside reconstruction.

| Demo | Default | Reconstruction choices | Guidance |
| --- | --- | --- | --- |
| SimpleLight / MegaLights | UDR4 Balanced | None, UDR1–4 | HRC geometry and temporal buffers |
| Rendering2D / CornellBox flat, raster, path tracing | None on startup | None, UDR4 | Spatial reconstruction |
| CornellBox radiance cascades | UDR4 Balanced on first activation | None, UDR4 | Spatial reconstruction; 16³ lighting probes |
| Tileset | None | None, UDR4 | Spatial reconstruction of cropped viewport |

Open **Image quality** in the framework inspector for reconstruction, quality, post AA, geometry AA where supported, and history reset. The UDR backend windows retain algorithm-specific advanced controls. Sources without a camera omit TAA. MegaLights also supports F11 to cycle None / UDR1–4.

`None` means native producer resolution with reconstruction disabled. `Native` retains the algorithm selection but bypasses reconstruction and returns the source texture directly. Performance and Balanced request 25% and 50% per dimension. Modern demos reduce the producer's view resolution, including its path tracer. Bypassing reconstruction restores an explicitly configured resolution. Tiles still rasterizes its padded world grid at tile resolution and reduces the final viewport; this does not reduce terrain iteration or CPU lighting work.

Cornell applies the cascade defaults once, so switching away and back retains
subsequent user choices, including None or Native. Its cascade output size follows
`Meshes.ViewSize`; UDR scales gathering and later image stages, while the
world-space probe grid stays independent. Probe-field reuse after 32 bounce
updates is separate from reconstruction history. See
[the 3D cascade investigation](RadianceCascades3D.md) for invalidation and timings.

## Integration

Add `Upscaling` after post AA with `Input = aa.GetOutput` and `After = [aa]`. Feed `upscaler.GetOutput` into the buffer inspector or presentation. Add it as an inspector source to expose its input/output. Pass it to `AntiAliasingSettings.Upscaler` for the shared controls. Select with `upscaler.Select(UpscalingMode.UDR4)` and disable reconstruction with `Select(UpscalingMode.None)`.

For a Render-phase geometry producer, apply `geometry.Resolution = upscaler.ResolveViewSize(geometry.Resolution)` before rendering. The existing demo helpers illustrate HRC and tile integration. `HrcInput = true` must only be set for imagery derived from this scene's HRC producer. UDR1–3 require HRC buffers; unsupported selections fail before changing the active mode.

UDR4's standalone defaults are preserved. When embedded in an explicit image pipeline, the owner sets `Present = false`. Generic inputs use `UseHrcGeometry = false` and `Temporal = false`; this is a spatial fallback, without depth or motion reprojection. The spatial path preserves image alpha for compositing. It uses two full-size HalfVector4 targets and two passes, compared with four targets and four passes in temporal mode. Set the integration flags before initialization. Do not manually register the owned backends as additional ECS systems.

## Further unification

The remaining architectural boundary is the legacy global `UDRQuality` and `Renderer.RenderScale`. This implementation targets the interactive single-view demos; it does not provide independent quality settings for multiple simultaneous views. A follow-up should move quality, output dimensions and history invalidation into a per-view settings/context object, with legacy adapters for existing games.

Temporal reconstruction outside HRC needs a defined depth/motion/camera input contract and corresponding shader reprojection. Until that exists, the shared selector exposes spatial UDR4 and reports that limitation. Capability-specific choices keep the controls consistent without exposing combinations that lack their required buffers. Dedicated regression fixture scenes retain explicit system setups to isolate the behavior being tested.

## Validation

```sh
dotnet build radiant.sln -c Release -m:1
dotnet run --project tests/Radiant.Tests -c Release --no-build
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- lights --upscaling-regression
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- triangles --upscaling-regression
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- tiles --upscaling-regression
dotnet run --project tests/Radiant.3D -c Release --no-build -- --upscaling-regression --2d
dotnet run --project tests/Radiant.3D -c Release --no-build -- --upscaling-regression
dotnet run --project tests/Radiant.3D -c Release --no-build -- --radiance-cascades-regression
```

GPU captures go to `artifacts/upscaling`. The selector regression exercises all supported reconstruction modes, all quality levels, FXAA/SMAA composition, identity bypass, switching off/on, finite outputs, nonempty presentation and resizing. Existing UDR4 and AA regressions retain their motion/history and edge-specific assertions.

The cascade fixture writes to `artifacts/radiance-cascades` and checks the default
UDR4 Balanced selection, reduced producer/full-size output, resize, None/Native
bypass, re-enabling reconstruction, lighting-cache invalidation and 1080p output.
