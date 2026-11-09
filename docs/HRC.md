# 2D global illumination

`HRCGI` lights your 2D world in real time: every glowing shape is a light source, solid shapes cast soft
shadows, and colored glass tints the light that passes through it. No light lists, no shadow maps.

It's based on [Holographic Radiance Cascades](https://arxiv.org/abs/2505.02041) by Freeman, Sannikov and Margel.

## Light a scene

Add `Geometry`, which turns your entities into what the lighting needs, and `HRCGI`.

The following example shows a dark room with one lamp and a wall:

```csharp
public override void SetupECS()
{
    ECS.AddSystem<Geometry>();
    ECS.AddSystem<HRCGI>();
    base.SetupECS();
}

public override void SetupScene()
{
    Renderer.FitWorld(Renderer.VirtualSize / 2, Renderer.VirtualSize);

    var warm = new Color(255, 180, 100);
    LightFactory.CreateLight(ECS, new Vector2(1500, 1080), 60f, warm, warm);

    int wall = ECS.CreateEntity(new Vector2(1900, 700));
    ECS.AddComponent<Rectangle2D>(wall) = new Rectangle2D(new Vector2(60, 700));
    ECS.AddComponent<Material>(wall).Albedo = Color.Black;

    base.SetupScene();
}
```

## Control light with materials

Every shape with a `Material` takes part in the lighting:

| You want | Set |
| --- | --- |
| A light | `Emissive` to the light color; its alpha is the brightness |
| A solid wall | `Albedo = Color.Black` |
| Colored glass | `Albedo` to the glass color with partial alpha, e.g. `new Color(60, 255, 60, 160)` |
| Something light ignores | Leave the default white `Albedo` and black `Emissive` |

The following example shows a pane of green glass; light behind it comes out green:

```csharp
ECS.AddComponent<Material>(glass).Albedo = new Color(60, 255, 60, 160);
```

Moving or recoloring shapes updates the lighting the same frame.

## Pick a quality level

The **Holographic Radiance Cascades** window (F1 → View) offers four quality levels that trade detail for speed: **Performance**, **Balanced**,
**Ultra** (the default) and **Native**. Lower levels space the light probes further apart, which softens small
details.

Combine a lower `Renderer.RenderScale` with an upscaler such as [UDR4](UDR4.md) to keep large scenes fast and
sharp.

## Keep proportions on any window

Frame your world with `Renderer.FitWorld`. Lights, shapes and mouse coordinates then keep their proportions when
the window changes shape, from landscape to portrait.

## Use the result

`GetOutput()` returns the lit image in linear color, so an area evenly lit at brightness 1 reads as exactly 1.
Feed it to tone mapping before display.

The following example shows the classic chain: lighting, color management, then upscaling:

```csharp
var lighting = ECS.AddSystem<HRCGI>();
var color = ECS.AddSystem<ColorManagement>();
var upscaler = ECS.AddSystem<UDR4>();

// After base.SetupECS():
color.SetInputSource(lighting.GetOutput);
upscaler.SetInputSource(color.GetOutput);
```

## Try it

```sh
dotnet run --project tests/Radiant.Scenes -c Release -- lights      # One warm light
dotnet run --project tests/Radiant.Scenes -c Release -- triangles   # MegaLights: paint lights and blockers
```
