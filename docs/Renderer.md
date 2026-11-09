# Renderer

Everything you see goes through `Renderer`: batched shapes, sprites and text, full-screen shader passes and
render targets. It's available in every scene and system.

## Draw shapes

Shapes are collected into a batch and drawn together in a single call, so thousands of them stay cheap.

The following example shows how to draw a health bar:

```csharp
Renderer.DrawRect(new Vector2(20, 20), new Vector2(200, 16), Color.Black);
Renderer.DrawRect(new Vector2(22, 22), new Vector2(196 * health, 12), Color.Lime);
Renderer.DrawCircle(new Vector2(240, 28), 8f, Color.Gold);
Renderer.FlushShapes(); // Draws everything batched so far
```

`FlushShapes` takes an optional target, clear color and technique: `"Default"` (smooth edges), `"Sharp"` or
`"Emissive"`. For entities, you rarely need this: `Geometry` draws every shape component automatically.

## Draw sprites and text

The following example shows how to draw an icon and a centered title:

```csharp
var title = "Level complete";
var size = Renderer.MeasureString("Inter-Bold", 32f, title);

Renderer.BeginDraw();
Renderer.DrawSprite(Renderer.GetTexture("presets/icons/Checkmark"), new Rectangle(40, 40, 32, 32), Color.White);
Renderer.DrawString("Inter-Bold", 32f, title, new Vector2((Renderer.ScreenWidth - size.X) / 2, 120), Color.White);
Renderer.EndDraw();
```

Text renders crisp at any size. `Inter` and `Inter-Bold` are ready to use; load your own TTF with
`Renderer.LoadFont("Display", "fonts/Display.ttf")`. To draw sprites in world coordinates, pass
`Renderer.WorldTransform` to `BeginDraw`.

## Run a shader pass

Shaders live in `Content/shaders` and are loaded once by name.

The following example shows how to blur an image into a render target:

```csharp
Renderer
    .Reset()
    .SetShader("Effects/Blur")
    .Configure(SamplerState.LinearClamp)
    .SetTarget(blurred).Clear(Color.Black)
    .SetParameter("InputTexture", source)
    .SetParameter("BlurRadius", 4f)
    .Draw()
    .Commit();
```

Start each pass with `Reset` so earlier state can't leak in, and finish it with `Commit`.

## Render to a texture

The following example shows how to draw into an offscreen target and come back to the screen:

```csharp
var minimap = Renderer.CreateRenderTarget(256, 256);

Renderer.PushTargets();
Renderer.SetTarget(minimap).Clear(Color.Black);
// ... draw the minimap ...
Renderer.PopTargets();
```

Bind up to four targets at once with `SetTargets`. For iterative effects, `PingPong` alternates two targets and
returns the final one. See [textures and render targets](GraphicsResources.md) for formats and ownership.

To show a texture, `Blit(texture)` stretches it over the current target, and
`Blit(texture, target)` copies it at its native size into another target.

## Fit the world to the window

World coordinates live in a fixed 3840 × 2160 space (`Renderer.VirtualSize`). `FitWorld` centers any area in the
window and keeps its proportions when the window is resized.

The following example shows how to frame a level and convert the mouse into world coordinates:

```csharp
Renderer.FitWorld(levelCenter, new Vector2(1600, 900));

Vector2 cursor = Renderer.ScreenToWorld(Controls.Mouse.Position);
```

Use `WorldToScreen` to place UI over an entity.

## Trade resolution for speed

`Renderer.RenderScale` (0.25 to 1) lowers the internal resolution of lighting and effects. Pair it with an
upscaler to keep the image sharp; see [image quality](RenderingPipeline.md).

## Use built-in textures

`GetSolidTexture(color)`, `GetCircleTexture(diameter)` and `GetRoundedRectTexture(radius)` create textures once
and cache them. Register your own procedural shapes with `RegisterShapeTexture` and fetch them with
`GetShapeTexture(name, size)`.

## Learn more

- [Renderer and window](RendererContracts.md): window control and optional GPU features.
- [Blending and sampling](RenderStates.md): blend, sampler, depth and rasterizer states.
- [Rendering pipelines](Rendering.md): flat, lit and path-traced images, AA and tone mapping.
