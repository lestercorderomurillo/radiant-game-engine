# Modular rendering

Scenes and entities are dimension-neutral containers. An entity may have 2D components, 3D components, both, or neither. Installing a 2D system does not install any 3D system. Nothing enforces a single renderer for the entire game.

## Data and movement

| Component | Stored data | Size |
| --- | --- | --- |
| `Transform2D` | Vector2 position, float rotation, Vector2 scale, float draw layer | 24 bytes |
| `Transform3D` | Vector3 position, Euler rotation, Vector3 scale | 36 bytes |
| `Movement2D` | Vector2 velocity and acceleration | 16 bytes |
| `Movement3D` | Vector3 velocity and acceleration | 24 bytes |

Scale defaults to one. `ECS.AddComponent<T>` initializes new component values using their constructors, including when an archetype slot is reused. Existing values survive archetype migration.

`ECS.CreateEntity(Vector2 position, float layer = 0)` adds only `Transform2D`; the Vector3 overload adds only `Transform3D`. `SetPosition` has matching overloads and updates spatial lookups. A 2D draw layer is independent of XY position. Legacy spatial queries continue to use their third coordinate as the 2D layer, preserving terrain lookups.

`Movement2DSystem` and `Movement3DSystem` are independently installed, pausable fixed-update systems. They integrate only matching components, then synchronize spatial lookups on the main thread. Installing both does not couple two transforms on the same entity. A game can add a custom projection/synchronization system if it wants that relationship.

## Producers and image stages

| System | Responsibility |
| --- | --- |
| `MeshGeometry3D` | Collect enabled 3D meshes and a camera on the CPU; cache world triangles only when tracing asks for them |
| `Geometry3D` | Rasterize mesh color and optionally world normals / linear depth |
| `ShapeGeometry2D` | Collect opaque rectangles, circles and triangles, including 2D scale/rotation/layer; expose flat color and an orthographic triangle adapter |
| `FlatRenderer` | Forward geometry color with no copy or extra target |
| `RasterRenderer` | Light submitted geometry before sample resolve; alternatively light an image with an optional normal buffer |
| `PathTracer` | Progressive diffuse GPU path tracing from an `ITraceGeometry` adapter |
| `Denoising` | Optional spatial, temporal diffuse and neural HDR denoising |
| `PathTracingSettings` | Optional per-view path tracing and denoiser selector window |
| `ToneMapping` | Shared exposure, tone operators and optional output transfer |
| `ColorSettings` | Optional per-view color window, bound to a `ToneMapping` instance |
| `AntiAliasingSettings` | Optional per-view AA window, bound to `AntiAliasing` and optionally `RasterSampling` |
| `RasterSampling` | Per-view MSAA / SSAA configuration and projection jitter |
| `AntiAliasing` | FXAA, SMAA 1x, TAA and area supersample resolve |
| `RenderPresentation` | Draw a chosen result to a target and optional destination rectangle |
| `RenderInspector` | Expose a view's named buffers through the existing F1 inspector |

The geometry systems produce buffers; they do not present to the screen. Inspecting a normal buffer never changes what another stage gets from a geometry source. `IRenderSource.GetOutput()` is the stable primary output; `Buffers` provides named live getters that survive resize.

Flat is itself a rasterized unlit path. `RasterRenderer` is the separately selectable lit raster path. These names describe useful pipeline choices; they do not imply that flat bypasses rasterization.

For mesh/shape lighting, set `RasterRenderer.Geometry` to an `IRasterGeometry` adapter (`Geometry3D` or `ShapeGeometry2D`). This shades each primitive's covered samples into an HDR target, then lets hardware resolve MSAA. Averaging normals first and lighting afterward produces bright seams at hard-normal edges; the geometry path avoids that nonlinear operation order. Normals and other geometry buffers remain independent inspection/TAA inputs. The lit target allocates its own visibility attachment only when its adapter requires one, and is released with the renderer.

```csharp
var raster = ECS.AddSystem(new RasterRenderer { Geometry = geometry, Input = geometry.GetOutput, After = [geometry] });
```

`Input` remains the bypass when the renderer is disabled. Without `Geometry`, the renderer retains the image-lighting path using `Input` and optional `Normals` (+Z when absent). Use geometry submission for MSAA mesh lighting; resolved image normals no longer contain the individual surface samples needed for correct edge lighting.

The existing 2D `Geometry` / HRC / UDR pipeline remains available and separate from the new opaque shape adapter. Image stages also accept its texture outputs through delegates. A 2D game using HRC does not need `MeshGeometry3D`, `Geometry3D`, a tracing adapter, or a path tracer.

## Compose only what is needed

A minimal 3D flat pipeline:

```csharp
var meshes = ECS.AddSystem<MeshGeometry3D>();
var geometry = ECS.AddSystem(new Geometry3D { Source = meshes, After = [meshes] });
var flat = ECS.AddSystem(new FlatRenderer { Input = geometry.GetOutput, After = [geometry] });
ECS.AddSystem(new RenderPresentation { Input = flat.GetOutput, After = [flat] });
```

A minimal 2D flat pipeline:

```csharp
var geometry = ECS.AddSystem<ShapeGeometry2D>();
ECS.AddSystem(new RenderPresentation { Input = geometry.GetOutput, After = [geometry] });
```

Optional AA works with either source:

```csharp
var aa = ECS.AddSystem(new AntiAliasing { Input = geometry.GetOutput, After = [geometry] }, enabled: false);
ECS.AddSystem(new RenderPresentation { Input = aa.GetOutput, After = [aa] });
```

A disabled image system forwards its input. `ToneMapper.None` skips the tone curve but still applies exposure. At exposure 1 with Linear output transfer it forwards its input and releases any cached color target. Exposure-only processing preserves HDR values in HalfVector4; it does not clamp highlights. No image target is allocated until an enabled pass actually needs it. Changing a stage's enabled flag retains its cached target for reuse; call `Dispose()` to release it. The demo selectors release inactive renderer targets explicitly.

`System.After` names system instances, so two views can contain the same system types without sharing their dependencies. Dependencies are sorted once at ECS initialization; missing dependencies and cycles fail explicitly. Existing type-based ordering attributes are also supported and apply to all matching instances. `GetSystem<T>()` returns the last registered instance, so retain the references returned by `AddSystem` when composing multiple views.

Each presentation has its own input, target, destination and optional clear. Presentation without a destination fills its target; it does not clear unless requested. Separate instances can composite independent views. Geometry adapters accept an optional `Resolution`; their cameras and tracer outputs use that view size instead of requiring the global window resolution. There is no global renderer enum, global tone mapper, or mandatory post-processing chain.

## Shared color and settings windows

ACES, AgX and the other tone operators process image radiance, independently of the renderer or scene dimension. `ToneMapping` supports None, Reinhard, ACES (Narkowicz fit), ACES Fitted (Hill RRT/ODT fit), AgX, Filmic, Uchimura and PBR Neutral. The previous HRC label "ACES 2.0" referred to the Hill fit; it is now named ACES Fitted rather than claiming to implement the ACES 2.0 standard.

`Exposure` is a linear multiplier. `Transfer` selects Linear (default), sRGB or Gamma22 separately from the tone curve. Processing unpremultiplies coverage, applies exposure and the curve, applies the selected transfer once, and restores alpha. `None` with Linear transfer preserves HDR highlights; choosing None does not disable exposure. Raw buffer inspection continues to bypass display processing.

The processing systems have no mandatory UI. Add the same optional window systems for a 2D or 3D view:

```csharp
var color = ECS.AddSystem(new ToneMapping { Input = renderer.GetOutput, Mapper = ToneMapper.AgX, After = [renderer] });
var aa = ECS.AddSystem(new AntiAliasing { Input = color.GetOutput, After = [color] });
ECS.AddSystem(new ColorSettings { Id = "mainColor", Source = color });
ECS.AddSystem(new AntiAliasingSettings { Id = "mainAA", Source = aa, Sampling = sampling });
```

Both windows appear under F1 → View and start hidden. They edit the bound instances and synchronize changes made through code. Use distinct IDs (and optional Titles) for multiple views; each view can choose a different curve, exposure or AA mode. Omit `Sampling` for a post-AA-only window, or omit the window systems entirely without changing rendering. The Cornell Box and compact 2D demo use these shared windows; their scene panels retain renderer selection and buffer inspection.

The existing `ColorManagement` class is now a thin HRC/UDR compatibility adapter over `ToneMapping`: it processes during Update so UDR1–4 can consume this frame's result. `SetInputSource` and its Color Management window remain available. Defaults retain PBR Neutral + Gamma22. Set `SettingsId` to customize its window ID, or null for no compatibility UI. It uses the same operators, exposure, alpha handling and shader as render-phase `ToneMapping`; do not chain both processors unintentionally. New render-phase pipelines compose `ToneMapping` and `ColorSettings` explicitly.

```sh
dotnet run --project tests/Radiant.3D -c Release -- --rendering-settings
dotnet run --project tests/Radiant.3D -c Release -- --rendering-settings --2d
dotnet run --project tests/Radiant.Scenes -c Release -- --color-pipeline
```

These regressions exercise every operator/transfer combination through the shared windows in both demo scenes, check premultiplied HDR/alpha and exposure/transfer ordering, compare legacy Update processing with Render processing, and validate identity/disabled bypass, null input cleanup and UI synchronization. The HRC color regression additionally feeds every operator through the real HRC → ColorManagement → UDR4 chain, compares its color output against render-phase processing and checks UDR4 history accumulation.

## Optional geometry buffers

`Geometry3D.Channels` defaults to color only. Color includes a hardware depth attachment for visibility; that attachment is distinct from an inspectable depth texture. Request `GeometryChannels.Normals`, `GeometryChannels.Depth`, or `GeometryChannels.All` only when a consumer needs them. The Cornell Box requests all channels for its inspector. Separate shader techniques omit unrequested MRT outputs.

A missing optional buffer returns null. The inspector lists only available outputs from enabled stages. Tab cycles that same set in both demos; switching renderers returns to Final. Scalar depth previews display grayscale without changing the source texture. `RasterRenderer.Normals` is optional for flat 2D surfaces; when using 3D world normals, explicitly request that geometry channel.

`ShapeGeometry2D.Rasterize = false` keeps its CPU adapter/camera active for tracing while releasing its raster target. All per-entity source data remains 2D. Its temporary world triangles are tracing/rendering data, not `Transform3D` components.

## Path tracing

The generic input is `ITraceGeometry`: enabled world triangles, a revision, and a perspective or orthographic camera. Mesh and shape adapters implement that contract independently.

The tracer builds a median-split BVH only when geometry changes, uploads its nodes/triangles to a float texture, and traverses escape links on the GPU. Traversal is bounded and monotonic. Camera motion does not rebuild geometry. Camera, environment, bounce-count, geometry and resolution changes reset progressive history. No camera clears output and resets history. Changing the geometry adapter invalidates the cached BVH as well.

Diffuse paths use cosine-weighted hemisphere sampling, emissive surfaces, an environment and Russian roulette. Primary rays use the camera clip range. `SurfaceEmission` is an optional component for HDR mesh emission. The Cornell Box includes a ceiling emitter. Geometry remains opaque; this is a baseline diffuse tracer, without hardware RT acceleration, transmission, glossy BSDFs or direct-light importance sampling. Denoising is a separate optional stage. Expect visible noise at low sample counts. It is not a replacement for a production path tracer.

`ResolutionScale` controls tracer resolution independently of rasterization. HDR history uses Vector4 (float32) ping-pong targets: half precision loses small corrections after many frames and biases brightness and color. The two history targets cost 32 bytes per tracing pixel and are allocated only when tracing renders. The running-average window is bounded at 4096 samples to avoid unbounded history weights; random sample sequences continue beyond that point. Scene extraction checks component values each frame; dynamic geometry rebuilds the BVH, rather than performing a refit. Mesh GPU buffers remain cached for the renderer's lifetime.

`PathTracer.Channels` defaults to `TraceChannels.None`. Request Albedo, Normals, Depth, Emission, Sample or All for ray-derived surface buffers in either dimension. These use deterministic pixel-center primary rays and the existing BVH, independently of stochastic radiance history. Only requested targets are allocated; static surfaces are cached until camera, geometry or resolution changes. Depth is linear view depth divided by the camera far plane, with zero for misses. Normals are encoded world-space normals facing the primary ray. Emission retains HDR values. The demo inspectors request all channels and route tracer depth to TAA while raster geometry is inactive. `TraceChannels.Sample` adds a separate RGBA32F unaccumulated sample (16 bytes per tracing pixel), written alongside the progressive mean without tracing twice. It is optional; temporal denoisers need it, spatial and neural filters do not. `All` now includes Sample. Camera motion resets progressive averaging but advances the random sequence; geometry/environment/bounce/resolution changes and explicit resets also advance `RadianceRevision`, allowing temporal consumers to distinguish camera reprojection from invalid scene history.

The optional `ViewSize` provider supplies the base tracing resolution before `ResolutionScale`. Both demos use their view size here, so selecting raster MSAA/SSAA does not silently change path resolution or consume raster targets. Path tracing provides stochastic ray sampling; its image can additionally use FXAA, SMAA or TAA.

Shape tracing tessellates circles using `CircleSegments`. New shape rendering is opaque and untextured; the existing 2D pipeline retains its texture/transparency features. Shape color, raster lighting and trace albedo are interpreted as linear numeric RGB; tone mapping is independent of output transfer/gamma conversion. HRC/UDR and the opaque 2D/3D pipelines share the same color shader and operators.

Algorithm references: [PBRT's simple path tracer](https://pbr-book.org/4ed/Light_Transport_I_Surface_Reflection/A_Simple_Path_Tracer), [PBRT path termination](https://pbr-book.org/4ed/Light_Transport_I_Surface_Reflection/A_Better_Path_Tracer), and [NVIDIA's FXAA white paper](https://developer.download.nvidia.com/assets/gamedev/files/sdk/11/FXAA_WhitePaper.pdf). The shaders here are engine-specific implementations.

## Optional path denoising

The shared **Path tracing** window under F1 → View offers **None**, **Joint bilateral**, **À-trous**, **SVGF (temporal diffuse)** and optional **OIDN neural** in both demo dimensions. SDKs without working native integrations are listed as unavailable and cannot be selected; selecting them reports why and retains the current filter. The standalone Cornell Box launch starts with À-trous enabled; generic systems and regression fixtures default to None. Strength adjusts edge weighting; à-trous and SVGF expose 1–5 wavelet iterations. **Neural denoising settings** opens per-view OIDN quality, device, update interval and status controls. **Reset denoising history** invalidates reconstruction without resetting the raw tracer. Changing the filter preserves accumulated path samples and resets the demo's downstream AA history.

`Denoising` is an independent image system, not part of the tracer or a mandatory post-processing chain. It filters the tracer's linear HDR output before `ToneMapping`; `PathTracer.GetOutput()` and its accumulation/history buffers remain raw. `RenderInspector` exposes the Denoising input, output and optional intermediate target independently from the path tracing buffers. No filtered result is fed back into progressive accumulation.

The algorithms are engine adaptations of [Tomasi and Manduchi, Bilateral Filtering for Gray and Color Images (1998)](https://www.cs.jhu.edu/~misha/ReadingSeminar/Papers/Tomasi98.pdf) and [Dammertz et al., Edge-Avoiding À-Trous Wavelet Transform for Fast Global Illumination Filtering (2010)](https://jo.dreggn.org/home/2010_atrous.pdf). Joint bilateral uses one 5×5 Gaussian pass. À-trous uses a separable B3 kernel evaluated as 25 taps per pass, doubling tap spacing at each level and narrowing the radiance range. Both combine normal, linear-depth and albedo weights. Diffuse albedo is demodulated before filtering and restored afterward; an optional emission guide excludes directly emissive surfaces. Alpha is preserved and samples with different coverage are rejected. A conservative one-sided depth slope accepts slanted surfaces while protecting depth discontinuities.

Bind matching, pixel-aligned surface buffers explicitly. Request `TraceChannels.Albedo | TraceChannels.Normals | TraceChannels.Depth` on the tracer, plus Emission when needed. The demos already request All for inspection. Consumers do not silently change a producer's requested channels.

```csharp
var denoiser = ECS.AddSystem(new Denoising { Input = tracer.GetOutput, Mode = DenoiserMode.Atrous, Albedo = () => tracer.AlbedoTexture, Normals = () => tracer.NormalTexture, Depth = () => tracer.DepthTexture, Emission = () => tracer.EmissionTexture, After = [tracer] });
var color = ECS.AddSystem(new ToneMapping { Input = denoiser.GetOutput, After = [denoiser] });
ECS.AddSystem(new PathTracingSettings { Id = "mainPath", Source = tracer, Denoiser = denoiser });
```

The same API accepts perspective/orthographic path outputs or another HDR producer with compatible guides. `PathTracingSettings.Denoiser` is optional. Give independent views distinct window IDs and retain their system references. Raw tracing output remains `tracer.GetOutput()`; omit Denoising entirely if unwanted.

None forwards input with no filter allocation and releases cached filter targets. A null input also releases targets, so the demos free denoising resources when switching away from tracing. Bilateral allocates one RGBA32F target (16 bytes per tracing pixel); multi-pass à-trous uses two (32 bytes per pixel). These costs exclude producer-owned guides. Spatial modes allocate no temporal history or neural model. SVGF and OIDN allocate their own resources only when selected, and dispose them when switching modes. Disabled systems follow the standard image-stage bypass/cache behavior; `Dispose()` releases their cached targets.

All modes are biased reconstructions: increasing strength/iterations can smooth lighting detail, and sparse sampling still leaves residual noise/fireflies. Bilateral and à-trous filter the progressive mean spatially. SVGF consumes unaccumulated samples and maintains a separate temporal history. None of the filters improves the tracer’s underlying light-transport sampling.

### Temporal diffuse reconstruction

The SVGF backend adapts [Schied et al., Spatiotemporal Variance-Guided Filtering (2017)](https://research.nvidia.com/labs/rtr/publication/schied2017spatiotemporal/): diffuse demodulation, camera reprojection with per-tap bilinear geometry rejection, luminance moments, 7×7 guided variance bootstrap for short histories, 3×3 variance prefiltering, dilated B3 wavelets and propagated variance. The first wavelet level feeds the next frame’s independent illumination history. Inspect **Temporal illumination** (variance in alpha), **Luminance moments** (mean, second moment, valid count), **First-level history**, **Surface history** (encoded world normal, normalized depth) and **Wavelet intermediate**.

Bind temporal inputs explicitly; the same contract works with perspective and orthographic adapters:

```csharp
tracer.Channels = TraceChannels.All;
denoiser.SingleSample = () => tracer.SampleTexture;
denoiser.Camera = () => tracer.Geometry.HasCamera ? tracer.Geometry.Camera : null;
denoiser.Revision = () => tracer.RadianceRevision;
denoiser.Mode = DenoiserMode.Svgf;
```

Only camera motion reprojects history. Scene revisions, projection/lens changes, large translation cuts, missing cameras, resizing and explicit resets invalidate it. Moving objects conservatively invalidate the whole scene; this implementation has no per-object motion/mesh-ID rejection or specular lobe separation. It is a diffuse adaptation, rather than the complete original diffuse/specular algorithm. Current progressive coverage, misses and directly visible emitters bypass reconstruction; raw path sampling/AA still determines their edge quality.

The backend uses six RGBA32F targets (illumination, moments ping-pong, feedback, output and optional wavelet scratch) and two HalfVector4 surface-history targets: **112 bytes per tracing pixel** at 3–5 wavelet levels, or 96 at 1–2. Producer guides and the 16-byte single-sample target are additional. Keep tracing resolution appropriate to the frame/memory budget; selecting SVGF at full 4K is expensive. Games using only 2D rasterization allocate none of these buffers.

### Optional neural reconstruction

[Open Image Denoise](https://www.openimagedenoise.org/documentation.html) is a real optional native RT neural filter, not a shader approximation. Install the pinned official 2.5.1 runtime locally and rebuild/restart:

```sh
python3 eng/install-oidn.py
dotnet build radiant.sln -c Release -m:1
```

The installer verifies the official release SHA-256 and copies platform libraries/licenses into ignored `Native/oidn/`; project references copy them into application output. Python 3.12+ is recommended. An existing runtime can instead be located with `RADIANT_OIDN_LIBRARY` (full library path). No model/library is required to build or use the spatial/temporal modes.

OIDN exposes Fast / Balanced / High quality and Auto / CPU / GPU device preferences. Auto uses OIDN’s first supported device; an unavailable explicitly requested device reports a failure and forwards raw radiance. Native inference runs on one worker per view, with reused host arrays and native buffers/filter; CPU inference uses half the logical processors. Status reports the actual device and inference time, excluding render-thread transfers and initialization. Increase the update interval to reduce inference/readback frequency. The backend reads completed HDR/guide images during Update, then uploads completed results and composites current coverage/misses/emitters during Render. No GPU APIs run on the worker. Changed cameras, scene revisions, dimensions or quality/device preferences invalidate output and request cooperative cancellation through OIDN’s progress monitor; obsolete completions are always discarded. Update only captures inputs matching the last rendered frame’s metadata, so an explicit producer reset cannot relabel an old image as a new scene epoch; raw radiance is used until a matching result completes.

This integration uses **host readback/upload**, including when OIDN chooses a GPU. Readback can stall rendering and worker results lag behind progressive sampling. It is useful for neural previews and quality comparisons, not a claim of zero-copy real-time performance. Continuous camera movement rejects old neural results; prefer temporal SVGF for responsive motion. GPU inference additionally needs a device/driver supported by OIDN. The regression validates native CPU inference and recovery after unavailable GPU selection. On the tested Linux installation OIDN enumerated no supported GPU device; installing a Vulkan driver alone does not supply OIDN’s GPU runtime requirements. GPU performance remains hardware/runtime dependent. **Neural output** is inspectable once a matching inference result exists.

### Backend extensions and 2026 SDKs

There is no universal “best denoiser”: reconstruction quality depends on signals, motion, supported hardware, sampling and frame budget. [DLSS 4.5 Ray Reconstruction](https://www.nvidia.com/en-us/geforce/news/dlss-4-5-ray-reconstruction-1000-rtx-games-apps-out-now/), [FSR Ray Regeneration](https://gpuopen.com/amd-fsr-sdk/), [NRD REBLUR/RELAX](https://github.com/NVIDIA-RTX/NRD) and OptiX are distinct vendor/native integrations. SIGMA in NRD handles shadows, rather than arbitrary beauty-image denoising. These are not extra names for the portable filters above.

NRD needs native compute dispatch/resource integration and additional signal conventions, including separated lobes/hit distances. DLSS/FSR require their supported native APIs/platforms, signals and hardware. OptiX requires NVIDIA CUDA/OptiX. Radiant’s current MonoGame renderer exposes neither compute dispatch nor native resource/command-list interop, so **none of those SDKs is integrated**. They appear as unavailable capabilities, with reasons; this does not promise identical image quality or performance to a production Cyberpunk renderer. Adding native compute/interop and richer tracing signals is the next prerequisite.

`IDenoisingBackend` provides per-view Render/Update, output/buffers, status/capability, reset and disposal. Register an independently owned, lazy backend factory; a small disposable probe supplies metadata. Distinct IDs are required, including when overriding an unavailable catalog entry:

```csharp
denoiser.RegisterBackend("studio.neural", () => new StudioDenoiser());
if (!denoiser.TrySelectBackend("studio.neural", out string reason)) Console.WriteLine(reason);
```

`GetOptions()` returns cached capability descriptors and `BackendId` is a stable string. The settings window refreshes after registration. Custom backend buffers are discovered by `RenderInspector` when they appear, keep existing buffer indices stable and disappear from available selections when released. Factories must return a fresh instance on each activation and defer GPU allocation until rendering. Backends never share mutable history across views.

```sh
dotnet run --project tests/Radiant.3D -c Release -- --denoising-regression
dotnet run --project tests/Radiant.3D -c Release -- --denoising-regression --2d
dotnet run --project tests/Radiant.3D -c Release -- --advanced-denoising-regression
dotnet run --project tests/Radiant.3D -c Release -- --advanced-denoising-regression --2d
```

The regressions exercise the normal ECS render/presentation path. Synthetic cases check known noise reduction, constant HDR preservation, alpha, normal-only/depth-only/albedo edges, direct emission, and even/odd wavelet pass counts. Real cases compare a fixed 4-sample input and both filters against 256-sample references, verify raw input/sample isolation, and exercise selector callbacks, FXAA/SMAA/TAA composition, missing cameras, resolution changes, renderer switching and cleanup. The simple 2D fixture has constant lighting in its interiors, so quality improvement is asserted on Cornell Box and synthetic noise rather than claiming to remove nonexistent 2D interior noise. The advanced cases additionally exercise real temporal history/reprojection, variance, wavelet pass counts, backend registration/disposal/buffers, unsupported selections and optional native OIDN Fast/Balanced/High execution. The temporal quality comparison uses diffuse interiors away from directly visible emitters and guide discontinuities. Captures are saved to `artifacts/denoising/`.

## Examples and validation

```sh
dotnet run --project tests/Radiant.3D -c Release
dotnet run --project tests/Radiant.Scenes -c Release -- rendering2d
```

R switches flat / raster / path tracing in either demo. F1 opens the framework controls. Geometry AA and Post AA have separate dropdowns; exposure remains active with Tonemapper None. Additional AA tuning and path tracing controls are available in their own windows from View. Selecting a raw buffer intentionally bypasses final exposure and AA; the inspector identifies this explicitly.

The entity inspector searches IDs and component names. Selecting one entity opens generic Entity Properties: choose a component and public field, type individual axes or adjust them with Step. Ctrl+A replaces the input, Enter commits and releases text focus, and live external changes refresh unfocused fields. Invalid numeric values and degenerate camera settings are rejected. Selection follows entity IDs across filtering and archetype changes; deleting the selected entity clears its editor state.

Camera controls uses `ICameraView` bindings rather than a global active camera. Mesh and shape views implement this contract, so each view can choose its own camera entity. Pose comes from a matching optional transform, otherwise from camera fields; lens settings remain in the camera. Custom components can use the same public-field editor. Boxed ECS access is confined to tooling; typed runtime queries remain unchanged. Inspector changes are runtime changes and are not saved to scene source files.

```sh
dotnet run --project tests/Radiant.3D -c Release -- --editor-regression
dotnet run --project tests/Radiant.3D -c Release -- --editor-regression --2d
dotnet run --project tests/Radiant.3D -c Release -- --editor-regression --navigate
dotnet run --project tests/Radiant.3D -c Release -- --editor-regression --2d --navigate
```

Editor regressions cover real GPU output after camera/component edits, per-view camera binding, optional transforms, tracing invalidation and camera deletion. Navigation cases drive controlled framework keyboard/mouse/text states through the normal inspector handlers, including Ctrl+A, Enter, navigation toggles and RMB + WASD. Captures are saved to `artifacts/editor/`.

```sh
dotnet build radiant.sln -c Release -m:1
dotnet run --project tests/Radiant.Tests -c Release --no-build
dotnet run --project tests/Radiant.3D -c Release --no-build -- --pipeline-regression
dotnet run --project tests/Radiant.3D -c Release --no-build -- --pipeline-regression --2d
dotnet run --project tests/Radiant.3D -c Release --no-build -- --regression
dotnet run --project tests/Radiant.3D -c Release --no-build -- --mixed-regression
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- triangles --smoke
```

The mixed-dimension GPU regression renders one entity into two independent 2D/3D views, each with its own AA instance and resolution. The pipeline GPU regressions exercise real triangle hits, renderer switching, optional FXAA, tone mapping, camera/geometry/environment invalidation, missing-camera clearing, BVH reuse, lazy resources and resize in both dimensions. Captures are saved to `artifacts/pipeline/`. Headless tests check component sizes/defaults, independent transforms, movement, BVH packing and per-instance dependencies.

## Migration

The former shared `Transform` is removed. Change 2D components/queries to `Transform2D`, use `Vector2` positions, and move the former ordering Z into `Layer`. Change mesh components/queries to `Transform3D`. `MotionTrackable` now stores Vector2 position history for the 2D temporal pipeline. All in-repository scenes and tests are migrated; external games referencing the old component need the same source changes.

`Geometry3D` now requires an explicit `MeshGeometry3D` source and a separate presentation system. Its output is always unlit color. Preview selection moved to `RenderInspector`; no rendering system owns the global inspector.

## Antialiasing families

Geometry sampling and post-processing are independent per-view systems:

| Geometry AA (`RasterSampling.Mode`) | Actual operation |
| --- | --- |
| None | Native-resolution rasterization |
| MSAA2x / MSAA4x / MSAA8x | Multisampled render targets, hardware resolve, and adapter capability fallback |
| SSAA2x / SSAA3x | Two/three times each view dimension (4/9 spatial samples per final pixel), then area-weighted resolve |

| Post AA (`AntiAliasing.Mode`) | Actual operation |
| --- | --- |
| None | Bypass; resolve/upscale only when input and view resolutions differ |
| FXAA | Contrast-based directional spatial filtering |
| SMAA | Reference SMAA 1x: edge detection, area/search lookup blending weights, neighborhood blending |
| TAA | Eight-position Halton projection jitter, depth-based camera reprojection, depth rejection, neighborhood clamping and history accumulation |

MSAA can be combined with a post filter such as SMAA. SSAA can be combined with TAA. `RasterSampling.ActualSamples` and the inspector report the real target sample count: on the tested RADV GPU, requesting 8x selects 4x. MSAA requires geometry integration; selecting it does not apply a renamed image blur. The native Vulkan backend's MSAA resolve supports one color attachment per render pass, so multisampled 3D color/normals/depth use separate geometry passes. Non-MSAA rendering retains the MRT path.

Minimal sampling configuration:

```csharp
var sampling = ECS.AddSystem(new RasterSampling { Mode = RasterAAMode.MSAA4x });
var meshes = ECS.AddSystem(new MeshGeometry3D { Sampling = sampling });
var geometry = ECS.AddSystem(new Geometry3D { Source = meshes, Channels = GeometryChannels.All, After = [meshes] });
var aa = ECS.AddSystem(new AntiAliasing { Input = geometry.GetOutput, Mode = PostAAMode.TAA, Camera = () => meshes.Camera, Depth = () => geometry.DepthTexture, GeometryRevision = () => meshes.Revision, OutputSize = () => meshes.ViewSize, After = [geometry] });
aa.SetSampling(sampling);
```

The 2D adapter accepts the same sampling system. Request `ShapeGeometry2D.CaptureDepth` when using TAA with camera reprojection. `SetSampling` connects temporal jitter; it does not enable a global camera effect. Sampling and AA can be omitted entirely from other views. MSAA does not apply to ray intersections; the path tracer already jitters camera rays and accumulates samples. The demos disable extra camera jitter for path tracing to preserve its accumulation.

TAA takes an optional normalized-UV motion texture (`previousUV - currentUV`) for object motion. Without one, the demos conservatively reset history on geometry revisions. Camera cuts, output resize, source format changes, disabling AA and AA mode changes reset history. History is validated using linear view depth; raw image inputs without depth use neighborhood-clamped temporal accumulation, without camera reprojection. Automatic projection jitter requires depth. Without depth or motion, camera changes discard history. This is a baseline TAA implementation, not a vendor reconstruction SDK or a guarantee of ghost-free output for every content type.

SMAA source, license, lookup textures and integration notes are in [the vendored reference directory](../Content/shaders/AA/SMAA/README.md). No lookup resource or temporal history is allocated until its mode is used. History, edge and weight buffers are inspectable. Returning to None releases the mode's intermediates. Geometry MSAA/SSAA targets are recreated when their settings change.

Vendor methods such as DLAA, TXAA and reconstruction SDKs are not advertised as implemented: they require their own native SDK/backend integration. The available modes cover the main spatial, morphological, multisample, supersample and temporal families, rather than presenting aliases for unsupported proprietary algorithms.

## Raster visibility stability

The stationary Cornell Box reproduced frame-dependent edge pixels with the Depth24 attachment on the tested MonoGame 3.8.6 Vulkan/RADV setup. Changing the hardware visibility attachment to Depth16 eliminated the changes while preserving independent CPU triangle-reference visibility. `Geometry3D.VisibilityFormat` is configurable; Depth16 is the tested stable default. Other drivers or depth ranges may warrant another format. This setting is separate from the inspectable Single-format linear depth texture.

Opaque meshes also cull back faces by default. With culling disabled, quantized depth ties let hidden back faces overwrite visible faces at silhouettes, producing dark dashed edges even in a stationary image. Both geometry buffers and raster lighting use the same per-mesh culling, reversing winding for negative-determinant transforms. Set `Mesh3D.DoubleSided = true` explicitly for thin/open surfaces that require both sides rasterized; path tracing retains its existing two-sided intersections.

The depth format change trades precision for stable behavior on this backend; use a sensible near/far range, and select another supported format explicitly where needed. The raster shader also marks clip positions precise to keep shared-edge transformations consistent across techniques. AA smooths sample coverage; it is not used to conceal incorrect visibility.

```sh
dotnet run --project tests/Radiant.3D -c Release -- --raster-stability
dotnet run --project tests/Radiant.3D -c Release -- --raster-edges
dotnet run --project tests/Radiant.3D -c Release -- --raster-visibility
dotnet run --project tests/Radiant.3D -c Release -- --aa-regression
dotnet run --project tests/Radiant.3D -c Release -- --aa-regression --2d
dotnet run --project tests/Radiant.3D -c Release -- --aa-regression --raster
dotnet run --project tests/Radiant.3D -c Release -- --aa-regression --path
dotnet run --project tests/Radiant.3D -c Release -- --aa-regression --2d --raster
dotnet run --project tests/Radiant.3D -c Release -- --aa-regression --2d --path
dotnet run --project tests/Radiant.3D -c Release -- --accumulation-regression
```

FXAA and the SMAA edge wrapper also detect alpha-coverage edges, including black geometry on transparent targets; neighborhood filters preserve premultiplied color and alpha. Tone mapping unpremultiplies partial coverage before its nonlinear curve and restores premultiplication afterward.

The AA regressions verify every mode, combined geometry/post-AA configurations, actual sample counts, output dimensions, fractional edge coverage, nonempty SMAA weights, finite temporal history exposure values without a tone curve, and black-on-transparent silhouettes without RGB contrast. Captures are saved to `artifacts/aa/`. The stability regression compares stationary frames and over 86,000 interior pixel rays per frame against independent CPU triangle intersections.

The raster-edge regression uses adjacent cube faces whose diffuse lighting is analytically identical. It checks constant brightness across their shared edge and stable pixels across frames for None, MSAA2x/4x/8x and SSAA2x/3x. This catches lighting halos even when geometry coverage and AA smoke tests pass. Captures are saved to `artifacts/raster-edges/`.

Renderer-specific AA cases exercise flat, raster and HDR path tracing in both dimensions. Path cases also compare albedo, normals, depth and emission against independent CPU triangle intersections, validate available-buffer cycling and verify that raster sampling leaves tracing resolution unchanged.

The accumulation regression renders narrow emissive strips with analytically known mean radiance for 16,384 frames. It checks brightness beyond the 4096-sample averaging window, catching long-term HDR history rounding bias that short smoke tests miss.

Temporal algorithm reference: [NVIDIA's survey of temporal antialiasing techniques](https://research.nvidia.com/labs/rtr/publication/yang2020survey/).

The raster-visibility regression compares every pixel against independent CPU ray intersections at 1080p and 4K, allowing coverage mixtures only from surfaces intersecting that pixel. It exercises MSAA, SSAA, mirrored transforms, rear views and explicit double-sided surfaces, and checks stationary frames for flicker. This catches hidden-face leaks at silhouettes that an interior-only comparison misses. Captures are saved to `artifacts/raster-visibility/`.

Scene-specific inspector windows use `Inspector.CreateSceneWindow("cornellBox", "Cornell Box", Visible: false)`. View lists system windows first, then a noninteractive separator and all scene windows, preserving layout order within each group. Ordinary `CreateWindow` calls remain system windows by default; the optional `Category` argument supports explicit grouping.
