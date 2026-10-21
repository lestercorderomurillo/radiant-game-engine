# HRC viewport mapping and symmetry

Radiant implements four directional Holographic Radiance Cascades, with separate RGB radiance and transmittance targets. The reference is [Freeman, Sannikov, and Margel's HRC paper](https://arxiv.org/abs/2505.02041), especially section 4.2 and Algorithm 1. The [authors' reference implementation](https://github.com/entropylost/amitabha) links to [Yaazarai's texture-based implementation](https://github.com/Yaazarai/Volumetric-HRC/tree/main/HolographicRadianceCascades), whose extension, cone merging, and fluence passes closely match this engine's organization.

## The rectangular-viewport bug

Geometry renders into a screen-shaped texture. Previously, `HRC_FrustumSeed` sampled that entire rectangle using normalized coordinates from a square power-of-two grid. A pixel step along X therefore represented a different screen distance from a step along Y. `HRC_MergingCones` nevertheless calculated angular weights with `atan(y / x)` as if those distances were equal. Stretching the square result back to the viewport preserved the ellipse in the lighting. A centered circular white emitter at 960 × 540 produced a 67.5% min-to-max angular spread on the outer test ring.

There was a separate demo projection problem: both lighting scenes used the default independent X/Y world scales. Resizing from 16:9 to 16:10 stretched even the emissive geometry.

## Coordinate contract

- `WorldSize` is the padded, square power-of-two cascade domain. Extension distances and cone angles use this grid's pixels.
- `ViewSize` is `Renderer.ScaledSize`. The rectangular scene occupies the top-left part of the domain; the remainder contains zero radiance and unit transmittance.
- Seeding first transforms the probe into its world quadrant, then maps square-grid UV to scene UV with `WorldSize / ViewSize`. Linear filtering avoids directional nearest-neighbor bias when sampling the reduced-resolution scene. Samples outside the scene are empty space, rather than clamped copies of its boundary.
- Composition maps viewport UV into the square domain with `ViewSize / WorldSize`. The one-pixel outward offsets remain in cascade pixels, as required by section 4.2 to avoid overlapping diagonal directions.
- `GetOutput()` returns a rectangular `ScaledWidth × ScaledHeight` target. Tone mapping and all upscalers already accept actual input dimensions. Consumers must not assume the output is the padded square.
- Resizing within the same power-of-two bucket must still recreate the viewport output and update its mapping.
- Probe row counts are integers. In the 3-pixel quality setting, a power-of-two height is not divisible by three; all passes use the actual spacing `WorldSize.Y / probeRows`, and shader dimensions match allocated textures exactly.
- SimpleLightScene and MegaLightsScene use `Renderer.FitWorld` so geometry and mouse input keep the same proportional, centered view through resizing.

The ray-extension equations and cone-merge angular weights were reviewed against the references. Changing them is not necessary to fix the viewport metric. Radiant retains its existing radiance scale, RGB material transmission, and approximation of seeding cascade zero from the scene rather than tracing the first three cascades as the paper does. This is still a discretized approximation; low probe densities can retain small angular differences around small emitters.

## Validation

```sh
dotnet build radiant.sln -c Release
dotnet run --project tests/Radiant.Tests -c Release --no-build
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- --lighting-regression
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- lights --smoke
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- triangles --smoke
```

The GPU regression reads linear half-float HRC output, before tone mapping. It samples 16 angles on three concentric rings, checks output dimensions after resizing, and exercises landscape, portrait, square, all three render scales, and all four probe qualities. The spread limit is 20%, allowing coarse-grid discretization while rejecting the original 67.5% error. Opaque blockers must suppress all three channels in each cardinal direction; green glass must transmit green while blocking red and blue. Captures are saved under `artifacts/lighting`.

Smoke tests additionally assert centered, proportional demo projection and input coordinate mapping, and save unedited captures of the actual Vulkan scene targets. Pac-Man's rendering suite checks the rectangular lighting/tonemapping pipeline, all upscalers, Native bypass, resize, character overlays, and maze emission/occlusion. Its old RCGI selection was removed to match the engine's HRC-only API.

On a headless X11 test display with RADV, use `SDL_VIDEODRIVER=x11 MESA_VK_WSI_DEBUG=sw` if Xvfb lacks the presentation path required by Vulkan. This selects the software **presentation** path; shader execution still uses the selected Vulkan GPU. Set `DISPLAY` explicitly to the isolated display, never to the user's desktop.
