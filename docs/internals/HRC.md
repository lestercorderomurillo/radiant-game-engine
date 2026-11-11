# HRC viewport mapping and symmetry

Radiant implements four directional Holographic Radiance Cascades. Ray extensions store separate RGB radiance and transmittance; descending cone merges store only radiance. The reference is [Freeman, Sannikov, and Margel's HRC paper](https://arxiv.org/abs/2505.02041), especially section 4.2 and Algorithm 1. The [authors' reference implementation](https://github.com/entropylost/amitabha) links to [Yaazarai's texture-based implementation](https://github.com/Yaazarai/Volumetric-HRC/tree/main/HolographicRadianceCascades), whose extension, cone merging, and fluence passes closely match this engine's organization.

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
- RadianceScene uses `Renderer.FitWorld` so geometry and mouse input keep the same proportional, centered view through resizing.

The ray-extension equations and cone-merge angular weights were reviewed against the references. Changing them is not necessary to fix the viewport metric. Radiant retains RGB material transmission and the approximation of seeding cascade zero from the scene rather than tracing the first three cascades as the paper does. This is still a discretized approximation; low probe densities can retain small angular differences around small emitters.

## Radiance normalization

`GetOutput()` returns linear RGB **mean radiance**: the angular integral over all four quadrants divided by `2*pi`. The cone weights already measure each child cone's angle, and partition a quadrant of `pi/2` radians. They do not receive an additional `0.5` factor. A uniform opaque emitting field of linear radiance 1 therefore produces 1, within half-float rounding error.

Previously composition divided by four, producing approximately `pi/2` for that calibration field. The normalized output is `2/pi` (about 0.63662) times the old output, or about 0.65 stops darker before tone mapping. This is an intentional output-scale change; consumers needing the old appearance can multiply the linear lighting by `pi/2` before tone mapping. Opaque blockers and RGB transmission retain their existing material semantics.

## Cascade storage

Ray extensions still need both RGB radiance and transmittance. During descending merges, far-cone transmittance never contributes to radiance, so it is neither sampled nor stored. All merge levels have identical dimensions and depend only on the next level; two alternating radiance targets replace the per-level merge arrays. Each quadrant retains one radiance output for composition.

At a 1024 x 640 window, 50% render scale and Ultra probe quality, HRC texture payload falls from 51,634,176 to 30,662,656 bytes (49.24 to 29.24 MiB; 40.6% reduction), with 45 targets reduced to 25. These figures exclude geometry, upscalers and driver overhead. Before changing normalization, deterministic captures of the earlier lighting demo, the circular emitter, an opaque blocker and green glass had bit-identical linear RGB output with the storage optimization. This is a memory result, not a GPU timing benchmark.

## Validation

```sh
dotnet build radiant.sln -c Release
dotnet run --project tests/Radiant.Tests -c Release --no-build
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- --lighting-regression
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- radiance --smoke
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- --radiance-regression
```

The GPU regression reads linear half-float HRC output, before tone mapping. It samples 16 angles on three concentric rings, checks output dimensions after resizing, and exercises landscape, portrait, square, all three render scales, and all four probe qualities. The spread limit is 20%, allowing coarse-grid discretization while rejecting the original 67.5% error. Opaque blockers must suppress all three channels in each cardinal direction; green glass must transmit green while blocking red and blue. Uniform white, colored and black inputs additionally check absolute mean-radiance normalization across all four probe qualities, with an absolute per-channel tolerance of 0.005. Captures are saved under `artifacts/lighting`.

For reproducible visual comparisons between builds:

```sh
dotnet run --project tests/Radiant.Scenes -c Release -- --lighting-captures artifacts/hrc-captures reference
dotnet run --project tests/Radiant.Scenes -c Release -- --lighting-captures artifacts/hrc-captures radiance
```

These runs save unedited scene-target PNGs, linear HRC RGB values as little-endian float32 files (`.rgb32f`, row-major RGB), and JSON dimensions/allocation counts. Radiance uses a fixed animation phase and ignores input. The reference run also captures opaque and green-glass blockers. Use separate output directories for each build.

The radiance interaction regression replays placement, continuous strokes, erasing initial and painted dots, nonemissive painting and the twelve-light limit and outer-ring spacing, pause/resume, reset, recycled entity IDs, and input boundaries before and after resize.

Smoke tests additionally assert centered, proportional demo projection and input coordinate mapping, and save unedited captures of the actual Vulkan scene targets. Pac-Man's rendering suite checks the rectangular lighting/tonemapping pipeline, all upscalers, Native bypass, resize, character overlays, and maze emission/occlusion. Its old RCGI selection was removed to match the engine's HRC-only API.

On a headless X11 test display with RADV, use `SDL_VIDEODRIVER=x11 MESA_VK_WSI_DEBUG=sw` if Xvfb lacks the presentation path required by Vulkan. This selects the software **presentation** path; shader execution still uses the selected Vulkan GPU. Set `DISPLAY` explicitly to the isolated display, never to the user's desktop.
