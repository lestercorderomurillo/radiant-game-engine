# Universal Detail Reconstruction

Render fewer pixels and reconstruct an image at display resolution. Radiant provides four UDR algorithms behind the `Upscaling` system, with a separate quality setting that controls the input resolution.

All four accept a color image from any 2D or 3D pipeline: unlit rendering, raster lighting, a producer using prebaked GI, HRC, 3D radiance cascades, path tracing or your own renderer. Lighting and reconstruction are independent choices. Optional geometry and motion guidance describe the image's view; they do not determine which lighting technique you must use.

## Choose an algorithm

| Mode | Reconstruction | Required input |
| --- | --- | --- |
| **UDR1** | Spatial reconstruction and edge refinement; no frame history | Color image from a 2D or 3D pipeline |
| **UDR2** | Spatial reconstruction, with temporal accumulation when guidance is connected | Color image from a 2D or 3D pipeline |
| **UDR3** | Lanczos reconstruction and sharpening, with guided edge refinement and temporal accumulation when available | Color image from a 2D or 3D pipeline |
| **UDR4** | Spatial reconstruction, with geometry- and motion-guided temporal reconstruction when available | Color image from a 2D or 3D pipeline |

Use `UpscalingMode.None` to disable reconstruction. The four algorithms are not quality presets: choose the algorithm first, then choose its render resolution.

With only an image connected, every mode uses its spatial path and preserves image alpha. Matching guidance enables additional edge refinement and, in UDR2–4, temporal history. Compare the algorithms on your scene, especially moving silhouettes, transparent overlaps and lighting changes.

## Set the resolution

`UDRQuality` is shared by all UDR systems:

| Preset | Scale per axis | Pixels relative to native |
| --- | --- | --- |
| Performance | 25% | 6.25% |
| Balanced | 50% | 25% |
| Native | 100% | 100% |

Balanced is the default. Native keeps the chosen algorithm but bypasses reconstruction; None disables the stage. Both provide an unscaled reference when comparing image quality.

The following example shows how to select the Balanced resolution preset:

```csharp
UDRQuality.Set(1); // Balanced; 0 = Performance, 2 = Native
```

Changing this setting affects every active UDR system. These presets are independent of HRC's lighting probe quality and the 3D radiance cascade grid. A newly created `Upscaling` stage still needs an algorithm selected; the Balanced preset alone does not enable reconstruction.

## Connect an image pipeline

Place `Upscaling` after your color and antialiasing stages. It produces an output image; add `RenderPresentation` to display it.

The following example shows how to connect reconstruction and presentation:

```csharp
var upscaler = ECS.AddSystem(new Upscaling
{
    Input = aa.GetOutput,
    After = [aa]
});
upscaler.Select(UpscalingMode.UDR4);

ECS.AddSystem(new RenderPresentation
{
    Input = upscaler.GetOutput,
    After = [upscaler]
});
```

Here `aa` is the preceding antialiasing stage. You can select UDR1, UDR2, UDR3 or UDR4 without changing the producer. By default, no guidance adapter is enabled, so reconstruction uses only the image.

The following example shows how to resolve the producer's view size each frame:

```csharp
geometry.Resolution = upscaler.ResolveViewSize(geometry.Resolution);
```

`ResolveViewSize` remembers an explicit view resolution and restores it when scaling is bypassed. Pass this stage to `AntiAliasingSettings.Upscaler` to expose algorithm and quality controls in **F1 → View → Image quality**.

## Connect 3D radiance cascades

The Cornell demo enables **UDR4 → Balanced** on its first switch to radiance
cascades, including when launched with `--radiance-cascades`. It uses 16³ probes,
renders primary shading at half the width and height, and reconstructs to the
full display size. For example, a 1920×1080 window receives a 960×540 input.
Later renderer switches preserve your reconstruction choice.

In your own scene, use the image pipeline above, following
`RadianceCascades → ToneMapping → AntiAliasing → Upscaling → RenderPresentation`.
All four UDR algorithms accept this output. Without a compatible guidance adapter,
they use spatial reconstruction; the current 2D guidance adapter does not consume
the 3D cascade depth or motion buffers.

Keep references to the mesh adapter, lighting stage and upscaler in your scene.
The following example shows how to bind the lighting output size during setup:

```csharp
cascades.ViewSize = () => meshes.ViewSize;
upscaler.Select(UpscalingMode.UDR4);
UDRQuality.Set(1);
```

The following example shows how to apply the selected resolution in the scene's
`Update` hook, before the mesh adapter renders:

```csharp
meshes.Resolution = upscaler.ResolveViewSize(meshes.Resolution);
```

UDR reduces primary shading and subsequent image-stage work. Probe density,
angular resolution and the bounce-update budget remain independent controls in
[3D Radiance Cascades](HRC.md#3d-radiance-cascades). Reusing a settled probe field
is a lighting optimization; it does not enable temporal reconstruction in UDR4.

## Add optional guidance

Set `UseGeometryGuidance = true` before selecting an algorithm or initializing the stage to use this scene's `Geometry` buffers. This adapter expects matching 2D emission, absorption and, where used, distance and motion buffers. The source image can use any lighting technique, but the buffers must describe the same geometry and view.

The following example shows how to connect a color image with matching 2D guidance:

```csharp
var upscaler = ECS.AddSystem(new Upscaling
{
    UseGeometryGuidance = true,
    Input = color.GetOutput,
    After = [color]
});
upscaler.Select(UpscalingMode.UDR3);
```

Leave guidance disabled when only a color image is available, including a custom producer using prebaked GI. A depth texture or motion texture from another renderer is not automatically compatible with the current 2D adapter. Image-only reconstruction remains available in all four modes.

## Connect HRC lighting

`HrcInput` enables the existing HRC adapter: matching 2D geometry guidance plus HRC sampling jitter for UDR4. It is optional and does not control which algorithms can be selected.

The following example shows how to enable that adapter for an HRC image:

```csharp
var upscaler = ECS.AddSystem(new Upscaling
{
    HrcInput = true,
    Input = color.GetOutput,
    After = [color]
});
upscaler.Select(UpscalingMode.UDR4);
```

Set this flag before selecting a mode or initializing the stage, when `color` processes the scene's HRC output. For an existing scene using standalone `UDR4`, keep using `SetInputSource(color.GetOutput)`. Do not add a standalone UDR system alongside an `Upscaling` stage that already owns one.

## Handle motion and scene cuts

Guided UDR2, UDR3 and UDR4 reuse previous frames. Reset history after a level change, camera cut or teleport so an unrelated image is not carried into the new view.

The following example shows how to discard reconstruction history:

```csharp
upscaler.ResetHistory();
```

When using standalone systems, UDR2 exposes `ResetAccumulation()` and UDR4 exposes `ResetHistory()`. The shared `Upscaling` stage handles reset behavior for whichever algorithm is selected.

### UDR4 motion guidance

Add `MotionTrackable` to moving 2D rectangles, circles and triangles when using the 2D guidance adapter.

The following example shows how to enable motion tracking for a circle:

```csharp
int ball = ECS.CreateEntity(new Vector2(400, 300));
ECS.AddComponent<Circle2D>(ball) = new Circle2D(20f);
ECS.AddComponent<Material>(ball).Emissive = Color.White;
ECS.AddComponent<MotionTrackable>(ball);
```

Guided UDR4 tracks camera motion and rejects history where geometry or lighting changes. The HRC adapter also jitters lighting samples; geometry-only guidance does not enable HRC jitter. Adding `MotionTrackable` alone does not connect guidance to an image-only pipeline.

## Tune and compare

**Sharpness** controls edge contrast. **History length**, on temporal modes, trades smoothing for responsiveness. In UDR4, final sharpening is excluded from history so it does not accumulate each frame.

Check static detail, moving objects and abrupt light changes separately. Rotating or resizing geometry may appear softer while reconstructed from the current frame. Overlapping translucent objects moving in different directions can leave trails; reduce history when that tradeoff is visible.

- [Global illumination](HRC.md) — choose the image producer and its material model.
- [Image quality](RenderingPipeline.md) — combine reconstruction with antialiasing and tone mapping.
- [Rendering pipelines](Rendering.md) — connect stages and present their outputs.
