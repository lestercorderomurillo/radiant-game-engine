# UDR

Render fewer pixels and reconstruct an image at display resolution. Radiant provides four UDR algorithms behind the `Upscaling` system, with a separate quality setting that controls the input resolution.

## Choose an algorithm

| Mode | Reconstruction | Required input |
| --- | --- | --- |
| **UDR1** | Spatial reconstruction with geometry-guided edge refinement; no frame history | HRC-derived 2D image and geometry buffers |
| **UDR2** | Spatial reconstruction followed by temporal accumulation | HRC-derived 2D image and geometry buffers |
| **UDR3** | Lanczos reconstruction, edge refinement, temporal accumulation and final sharpening | HRC-derived 2D image and geometry buffers |
| **UDR4** | Geometry- and motion-guided temporal reconstruction; spatial fallback for other images | HRC-derived 2D image for temporal mode, or a generic 2D/3D image for spatial mode |

Use `UpscalingMode.None` to disable reconstruction. The four algorithms are not quality presets: choose the algorithm first, then choose its render resolution.

For a general 2D or 3D pipeline, start with UDR4's spatial mode. For HRC lighting, all four modes are available. Compare them on your own scene, especially moving silhouettes, transparent overlaps and lighting changes.

## Set the resolution

`UDRQuality` is shared by all UDR systems:

| Preset | Scale per axis | Pixels relative to native |
| --- | --- | --- |
| Performance | 25% | 6.25% |
| Balanced | 50% | 25% |
| Native | 100% | 100% |

Balanced is the default. Native keeps the chosen algorithm but bypasses reconstruction; None disables the stage. Both provide an unscaled reference when comparing image quality.

```csharp
UDRQuality.Set(1); // Balanced; 0 = Performance, 2 = Native
```

Changing this setting affects every active UDR system. These presets are independent of HRC's lighting probe quality.

## Connect an image pipeline

Place `Upscaling` after your color and antialiasing stages. It produces an output image; add `RenderPresentation` to display it.

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

Here `aa` is the preceding antialiasing stage. With the default `HrcInput = false`, UDR4 uses spatial reconstruction without temporal history. This is the supported path for generic raster, path-traced and 3D images.

Let the stage resolve the producer's view size each frame:

```csharp
geometry.Resolution = upscaler.ResolveViewSize(geometry.Resolution);
```

`ResolveViewSize` remembers an explicit view resolution and restores it when scaling is bypassed. Pass this stage to `AntiAliasingSettings.Upscaler` to expose algorithm and quality controls in **F1 → View → Image quality**.

## Connect HRC lighting

Set `HrcInput = true` **before selecting a mode or initializing the stage**, and only when its source actually comes from the scene's `Geometry`/`HRCGI` pipeline. An unrelated image cannot use HRC's geometry as valid guidance.

```csharp
var upscaler = ECS.AddSystem(new Upscaling
{
    HrcInput = true,
    Input = color.GetOutput,
    After = [color]
});
upscaler.Select(UpscalingMode.UDR3); // UDR1, UDR2 and UDR4 are also available
```

Here `color` processes HRC lighting. Selecting UDR1–UDR3 without `HrcInput` throws `InvalidOperationException`. UDR4 uses HRC geometry and temporal reconstruction when this flag is enabled.

For an existing HRC scene that uses the standalone `UDR4` system, keep using `SetInputSource(color.GetOutput)`. Do not add a standalone UDR system alongside an `Upscaling` stage that already owns one.

## Handle motion and scene cuts

UDR2, UDR3 and HRC-guided UDR4 reuse previous frames. Reset history after a level change, camera cut or teleport so an unrelated image is not carried into the new view:

```csharp
upscaler.ResetHistory();
```

When using standalone systems, UDR2 exposes `ResetAccumulation()` and UDR4 exposes `ResetHistory()`. The shared `Upscaling` stage handles reset behavior for whichever algorithm is selected.

### UDR4 motion guidance

Add `MotionTrackable` to moving 2D rectangles, circles and triangles:

```csharp
int ball = ECS.CreateEntity(new Vector2(400, 300));
ECS.AddComponent<Circle2D>(ball) = new Circle2D(20f);
ECS.AddComponent<Material>(ball).Emissive = Color.White;
ECS.AddComponent<MotionTrackable>(ball);
```

UDR4 tracks camera motion and rejects history where geometry or lighting changes. Its sampling jitter affects HRC lighting, not gameplay geometry, collisions or UI. Generic images use the spatial fallback and do not gain temporal reconstruction just by adding `MotionTrackable`.

## Tune and compare

**Sharpness** controls edge contrast. **History length**, on temporal modes, trades smoothing for responsiveness. In UDR4, final sharpening is excluded from history so it does not accumulate each frame.

Check static detail, moving objects and abrupt light changes separately. Rotating or resizing geometry may appear softer while reconstructed from the current frame. Overlapping translucent objects moving in different directions can leave trails; reduce history when that tradeoff is visible.

- [Global illumination](HRC.md) — choose the image producer and its material model.
- [Image quality](RenderingPipeline.md) — combine reconstruction with antialiasing and tone mapping.
- [Rendering pipelines](Rendering.md) — connect stages and present their outputs.
