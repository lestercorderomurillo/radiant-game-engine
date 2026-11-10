# 3D radiance cascades investigation

Status: experimental dense world-space implementation, continued from Claude's
unfinished working tree and validated on 2026-11-10. This stage is separate from
the existing 2D `HRCGI`. It is available in Cornell's renderer selector and through
`dotnet run --project tests/Radiant.3D -c Release -- --radiance-cascades`.

## Research and implementation choice

[Sannikov's original working paper](https://github.com/Raikiri/RadianceCascadesPaper/blob/main/RadianceCascades.tex)
describes radiance intervals and their composition, the tradeoff between spatial
and angular resolution, and an experimental world-space 3D grid. It explicitly
identifies dense volumetric storage as a scaling problem. Radiant follows that
grid organization, using triangle BVH queries rather than voxel traversal. This
is an engineering adaptation, not a reproduction of the paper's testbed.

[Osborne and Sannikov, RASTI 4, rzae062](https://academic.oup.com/rasti/article/doi/10.1093/rasti/rzae062/7929002),
especially Appendix A, explains merging a separately traced connecting interval
with each interpolated upper probe. The correction requires eight connecting
rays in 3D. Radiant's `LeakFix` applies this idea; it is not a guarantee against
all leaking when the spatial or angular representation is insufficient.

[Freeman, Sannikov and Margel, Holographic Radiance Cascades](https://arxiv.org/abs/2505.02041)
targets 2D global illumination. It supports the existing 2D engine path, but does
not by itself establish a practical 3D implementation or a transferable timing
claim. The new stage therefore does not extend the 2D HRC shaders.

[Freeman, Sannikov and Pitt-Francis, Split Radiance Cascades, v2](https://arxiv.org/abs/2607.20384v2)
(October 2026) describes sparse world-space probes and splitting surface-origin
rays into cascade contributions by hit distance. Its abstract identifies dense
volume cost as the obstacle to practical 3D RC. It is a relevant next research
direction, but this implementation does not implement that algorithm. Only the
abstract/version metadata was reviewed here; the full PDF could not be retrieved
through the browser tool, so no detailed implementation or performance claim is
inferred from it.

## Current pipeline

1. `TraceSceneBuffer` uploads a packed `TraceBvh` on source/revision changes. The
   buffer helper is reused by `PathTracer` and `RadianceCascades`; each system owns
   its own instance, so this is code reuse rather than a shared allocation cache.
2. Cascade grids halve each axis and double each angular dimension per level.
   Directions use an equal-area spherical mapping. Probe tiles live in 2D
   half-float atlases; the last cascade traces to infinity and supplies environment
   radiance on a miss. Interval endpoints grow by four per level.
3. `RC_Intervals.fx` resolves cascades from coarse to fine. Child rays start at
   their parent's endpoint direction. Connecting segments terminate at that same
   endpoint on the upper probe, keeping the corrected chain continuous.
4. `RC_Irradiance.fx` integrates cascade zero into an 8×4 normal-direction tile
   per probe, stored as 10×4 with wrapping border columns for hardware filtering.
   Discrete cosine weights are normalized to preserve a uniform field.
5. `RC_Gather.fx` traces full-resolution primary hits, interpolates irradiance
   with normal/visibility weights, applies diffuse albedo divided by pi, adds
   emission and writes linear depth. The irradiance ping-pong supplies the next
   frame's bounce feedback.

Corrections made during continuation:

- Cornell's direct-light baseline now comes from the first cascade frame. The
  previous frame-six baseline already contained bounce feedback, causing the
  reported failure when multiple bounces were disabled.
- Each connecting ray evaluates bounce feedback at its own hit. Combining
  albedos and sampling only the last hit mixed unrelated surfaces and normals.
- Geometry/source, environment, bias and bounce-mode changes invalidate feedback.
  Empty geometry and missing cameras clear output/depth and reset feedback.
- Raising `MaxCascades` now rebuilds after an earlier reduction. Explicit volume
  changes rebuild atlases; returning to automatic bounds refits them.
- The terminal cascade no longer binds its output texture as its own input.
- One direction row is rejected: all samples would lie on the equator and cannot
  integrate pole-facing normals. Unsupported atlas combinations fail before
  allocation, and the inspector keeps its choices within the supported limit.

## Accuracy and cost limits

The analytic furnace validates constant-field energy and temporal convergence;
it cannot establish accuracy for varying radiance. The Cornell checks establish
nonuniform illumination, color bleeding, environment/depth output and lifecycle
behavior, not agreement with an offline reference. No general error bound has
been measured. Thin geometry, coarse normal tiles, angular undersampling and
normal bias can still cause artifacts. With `LeakFix = false`, the furnace's
dark external environment leaks inward; this mode is explicitly approximate.

At the standalone stage's grid 32 and 4 direction rows, the four interval atlases
contain 1,966,080 texels (15 MiB), plus about 20.22 MiB for the two padded
irradiance atlases. The interval segment budget is 14,811,136 per probe-field
update with corrected merging, before primary rays and visibility queries.
`RaysPerFrame` reports only that interval budget; reused fields skip those rays.
These counts exclude scene BVH and screen-sized output/depth storage.

Before optimization, the corrected Cornell run at 640×360 averaged **60.09 ms per whole frame** over
frames 8–20 on Linux/RADV, RX 9070 XT. This is a short uncapped wall-clock sample
including the demo pipeline and presentation, not an isolated GPU timestamp or
a stable benchmark. Cornell now selects grid 16 for interactive evaluation.
No native ray-query path, sparse
allocation, probe relocation, clipmaps, glossy transport or temporal denoising
is implemented in this stage.

Further research should compare against converged path tracing on small emitters,
slopes, thin partitions and moving geometry, profile the individual GPU passes,
and evaluate sparse surface-driven probes before expanding to large scenes.

## Optimization and UDR defaults

Cornell's first activation of radiance cascades selects grid 16, UDR4 and Balanced
(50% per axis). Later renderer switches preserve reconstruction choices. The
standalone `RadianceCascades` stage keeps its grid-32 default. UDR operates after
tone mapping and AA through the existing generic spatial path; it does not reduce
the probe grid or add HRC temporal guidance. A 1920×1080 view therefore gathers
at 960×540 and reconstructs to the display size.

Visibility now returns on the first blocker without fetching materials or
searching for the closest hit. Normal-direction interpolation uses one filtered
fetch with explicit wrap columns instead of four point fetches. The dense/native
Cornell capture differs from the original by at most one 8-bit channel step,
with mean absolute difference 0.00066 on the 0–255 scale. This comparison is for
the shader changes at the same grid, not evidence that a 16³ grid matches 32³.

`MaxBounceUpdates` defaults to 32. After the budget, the stage reuses its
world-space field; primary gathering still follows the camera each frame.
Single-bounce lighting is reused after one update. Every existing lighting/layout
invalidation clears validity, including geometry revisions, environment and bias
changes. Camera motion and view resizing leave world-space lighting valid.
`ProbeUpdateCount` and `ReusedProbeField` expose the actual work. Zero means
continuous updates; increasing the budget resumes iterations. This fixed budget
is an approximation, not a GPU convergence test, and highly reflective scenes
may need more iterations. Inspector controls expose the limit and cached state.

Final fixture measurements on the same RX 9070 XT / Linux RADV device:

| Display | Grid / reconstruction | Updating field | Reusing field |
| --- | --- | --- | --- |
| 640×360 | 16³ / UDR4 Balanced | 25.96 ms | 0.44 ms |
| 1920×1080 | 16³ / UDR4 Balanced | 13.09 ms | 1.16 ms |
| 640×360 | 32³ / native | 56.82 ms | 0.69 ms |
| 1920×1080 | 32³ / UDR4 Balanced | 40.07 ms | 1.16 ms |

The fixture now drains GPU work at both timing endpoints with a one-pixel depth
readback, preventing queued submissions from being counted as completed frames.
These are short whole-frame samples, including CPU, presentation and endpoint
readback overhead; they are not GPU pass timestamps. The 640×360 sample is early
in the run; the 1080p sample follows the lifecycle tests, camera movement and an
environment change. The lower 1080p result must not be interpreted as resolution
scaling; clocks, warm-up and scene state differ. Reusing a field is cheaper than
recomputing it, and 60 FPS is not guaranteed during every update or on other GPUs.

## Reproducible validation

The Release build (zero warnings/errors), headless suite, portable suite, both
cascade fixtures, existing 3D pipeline fixture and stock-runtime Vulkan fallback
fixture all passed on the validation date. The Cornell bounced capture was also
visually inspected. Hardware ray queries were not revalidated in this run.
The optimization pass also validates field reuse, camera movement without probe
updates, emission changes after caching, update-budget changes, UDR off/on and
Native bypass, both the new default and dense/native mode, and 1080p output.

Build first, then run GPU fixtures sequentially on a Vulkan display:

```sh
dotnet build radiant.sln -c Release -m:1
dotnet run --project tests/Radiant.Tests -c Release --no-build
dotnet run --project tests/Radiant.Portable -c Release --no-build
dotnet run --project tests/Radiant.3D -c Release --no-build -- --radiance-cascades-regression --furnace
dotnet run --project tests/Radiant.3D -c Release --no-build -- --radiance-cascades-regression
dotnet run --project tests/Radiant.3D -c Release --no-build -- --radiance-cascades-regression --dense --native
dotnet run --project tests/Radiant.3D -c Release --no-build -- --pipeline-regression
dotnet run --project tests/Radiant.3D -c Release --no-build -- --vulkan-rt-regression --expect-software
```

The furnace uses unit emission and albedo 0.5, so frame n expects
`2 - 0.5^n`; half-float results reach about 1.998 at convergence. It also changes
emission, source, environment, bias, cascade limits, merge mode and volume,
and checks empty geometry. Cornell covers grid changes, resize, camera loss,
switching away/back, depth and bounce contribution. Captures are written to
`artifacts/radiance-cascades/cornell.png` and `cornell-bounced.png`.
Reconstructed captures are `cornell-optimized.png` and
`cornell-optimized-1080p.png`; each fixture run overwrites these diagnostics.
