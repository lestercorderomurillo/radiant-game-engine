# UDR4 upscaling

UDR4 renders lighting at a fraction of the resolution and reconstructs a sharp, stable full-resolution image.
It reuses detail from previous frames, follows moving objects and rejects stale history where geometry or light
changed.

## Add it to a lit scene

The following example shows how to chain lighting, color management and UDR4:

```csharp
var lighting = ECS.AddSystem<HRCGI>();
var color = ECS.AddSystem<ColorManagement>();
var upscaler = ECS.AddSystem<UDR4>();

// After base.SetupECS():
color.SetInputSource(lighting.GetOutput);
upscaler.SetInputSource(color.GetOutput);
```

Set the source once; you don't need to assign it every frame. UDR4 also accepts the lighting output directly.

## Track moving objects

Give moving rectangles, circles and triangles a `MotionTrackable` component so UDR4 can follow them instead of
smearing them.

The following example shows a ball that stays crisp while it moves:

```csharp
int ball = ECS.CreateEntity(new Vector2(400, 300));
ECS.AddComponent<Circle2D>(ball) = new Circle2D(20f);
ECS.AddComponent<Material>(ball).Emissive = Color.Orange;
ECS.AddComponent<MotionTrackable>(ball);
```

Camera movement is tracked automatically.

## Reset after a cut

Call `ResetHistory()` whenever the image jumps: a new level, a teleport, a respawn.

```csharp
public void LoadLevel(int index)
{
    BuildLevel(index);
    ECS.GetSystem<UDR4>().ResetHistory();
}
```

Resizing, quality changes and new inputs reset history automatically.

## Tune it

Pick the quality in **Image quality**: Performance (25% resolution per axis), Balanced (50%) or Native (no
upscaling). The UDR4 window adds:

- **Sharpness**: crispness of the final image. It never feeds back into history, so it can't build up artifacts.
- **History length**: more frames give smoother results; fewer react faster to change.

## Get the best results

- UDR4 subtly shifts where lighting is sampled each frame (`HRCGI.OutputJitter`). Geometry, collisions and UI are
  never shifted.
- Rotating, deforming and resizing objects are reconstructed from the current frame, which keeps them correct but
  can look softer while they change.
- Overlapping translucent objects moving in different directions can show faint trails; avoid long-lived stacks
  of them in fast scenes.
- At 1920 × 1080, UDR4 uses about 79 MiB of GPU memory.
