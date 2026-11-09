# Render states

Render states control how drawing blends with what's already there, how textures are filtered, and how
triangles are culled and depth-tested. Pick a preset, or describe your own in one line.

## Use the presets

The following example shows the most common configuration for UI and sprites:

```csharp
using com.radiant.engine.graphics;

Renderer.Configure(BlendState.AlphaBlend)
    .Configure(SamplerState.LinearClamp)
    .Configure(DepthStencilState.None)
    .Configure(RasterizerState.CullNone);
```

| Want | Use |
| --- | --- |
| Normal transparency | `BlendState.AlphaBlend` (expects premultiplied color) |
| Transparency with straight alpha | `BlendState.NonPremultiplied` |
| Glows and light | `BlendState.Additive` |
| Replace pixels | `BlendState.Opaque` |
| Crisp pixel art | `SamplerState.PointClamp` |
| Smooth scaling | `SamplerState.LinearClamp` |

## Make your own state

States are immutable descriptions, so you can create, keep and share them freely. Nothing needs disposing.

The following example shows an "eraser" blend and a red-only variant of the opaque preset:

```csharp
var erase = new BlendState
{
    ColorSourceBlend = Blend.Zero,
    ColorDestinationBlend = Blend.InverseSourceAlpha,
    AlphaSourceBlend = Blend.Zero,
    AlphaDestinationBlend = Blend.InverseSourceAlpha
};

var redOnly = BlendState.Opaque with { ColorWriteChannels = ColorWriteChannels.Red };
```

The renderer reuses GPU resources for equal descriptions, so define states once (for example as `static
readonly` fields) instead of building new variations every frame.

## Premultiplied alpha

`AlphaBlend` expects color that is already multiplied by alpha. Textures loaded from content are prepared this
way. When you generate pixels yourself with straight alpha, draw them with `NonPremultiplied`.

## Sprite options

`SpriteSortMode` controls sprite ordering inside `BeginDraw`, and `SpriteEffects` flips sprites horizontally or
vertically.
