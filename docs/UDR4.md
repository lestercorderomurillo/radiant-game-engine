# UDR4 temporal reconstruction

UDR4 reconstructs reduced-resolution HRC lighting using geometry-guided bicubic filtering, motion-reprojected history, neighborhood/variance clipping, and conservative rejection of changed geometry and lighting. It is a custom analytical upscaler, with no trained model or frame generation.

Pac-Man selects UDR4 by default at Balanced (50% per dimension). Its Pipeline inspector retains UDR1–3 for comparisons. Performance remains 25% and Native remains a direct bypass. UDR4 exposes sharpening and history length in its inspector window.

## Integration

Register `Geometry`, `HRCGI`, `ColorManagement`, and `UDR4`. `ColorManagement` is the Update-phase adapter for the same `ToneMapping` color operators and settings used by the generic 2D/3D pipelines; its defaults retain PBR Neutral and Gamma22. Connect the tonemapper to `HRCGI.GetOutput` and UDR4 to `ColorManagement.GetOutput`. The system ordering attributes put UDR4 after these producers. UDR4 also accepts linear HRC output directly. Add `MotionTrackable` to moving rectangles, circles, and triangles.

```csharp
lighting = ECS.AddSystem<HRCGI>();
tonemapper = ECS.AddSystem<ColorManagement>();
upscaler = ECS.AddSystem<UDR4>();
// After system initialization:
tonemapper.SetInputSource(lighting.GetOutput);
upscaler.SetInputSource(tonemapper.GetOutput);
```

Call `upscaler.ResetHistory()` after level changes, teleports, or other discontinuities. Pac-Man calls it when loading or restarting a level. Input texture replacement, input dimensions, quality changes, resizing, and reinitialization also reset history. A stable source delegate does not need to be assigned every frame. `GetOutput()` returns the current reconstructed texture, or the source in Native mode and before reconstruction is ready.

UDR4 controls `HRCGI.OutputJitter` while active. HRC samples its radiance field at eight subpixel offsets, measured in reduced-resolution output texels. Reconstruction removes the corresponding `LastOutputJitter`; geometry, collision coordinates, character overlays, and HUD remain unjittered. This samples the existing HRC field more densely; it does not increase cascade probe or ray resolution. A custom input producer must provide an unjittered texture when there is no HRC system. Do not connect unrelated imagery while allowing UDR4 to control an HRC producer.

## Motion and history

`Geometry.TemporalMotionTexture` stores signed, 32-bit floating-point world displacement, independently of the legacy encoded motion texture. Displacement uses consecutive rounded positions actually rendered by Geometry, rather than averaged velocity. It is collected with the same buffered geometry frame. Current and previous world-to-screen transforms account for camera movement. Motion silhouettes follow the rectangle, circle, and triangle primitives and are ordered by Z among tracked objects.

History is rejected outside the viewport and when an emissive/absorption signature changes. Radiance differences reduce history weight separately because moving light and shadow patterns can move independently of geometry. A 3×3 neighborhood and variance bounds constrain history before blending. The unsharpened result becomes history; bounded sharpening affects presentation only.

This implementation has no depth/object-ID buffer. Textured objects use their primitive motion silhouette, untracked moving objects rely on appearance rejection, and overlapping translucent objects cannot carry multiple velocities per pixel. Rotation/deformation and changes of primitive size are not tracked by the translation buffer. These cases deliberately favor current-frame appearance but can still show artifacts. This is not a claim of FSR-quality reconstruction.

Four RGBA16F targets plus the RG32F motion buffer consume approximately 40 bytes per output pixel (79 MiB at 1920×1080), excluding existing HRC/geometry buffers and driver overhead. Motion is disabled in Native mode and when UDR4 is disposed. The four full-screen passes have not yet been assigned a measured GPU time budget.

## Validation

```sh
dotnet build radiant.sln -c Release -m:1
dotnet run --project tests/Radiant.Tests -c Release --no-build
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- --udr4-regression
```

The headless checks exercise motion history wraparound, signed displacement beyond the legacy range, and jitter bounds/periodicity. The GPU regression checks finite startup, history accumulation/reset, moving silhouettes and disocclusion, camera mapping, and resizing against current geometry pixels. Pac-Man's rendering suite covers all four upscalers, all three quality settings, eye overlays, aspect ratios, Native bypass, and disabling/re-enabling upscaling. Its smoke suite covers all six levels, restart, pause, and switching back to UDR4.

On an isolated Xvfb display with RADV, use `SDL_VIDEODRIVER=x11 MESA_VK_WSI_DEBUG=sw` as described in the HRC notes. This changes presentation, not the GPU executing the shaders. GPU test artifacts are written under `artifacts/udr4` and Pac-Man rendering captures under `artifacts/rendering` when run from the engine directory.
