# Radiant render states

Import `com.radiant.engine.graphics` for `BlendState`, `SamplerState`,
`RasterizerState`, `DepthStencilState` and their enums. Renderer configuration,
sprite sort/flip options, presentation settings and the built-in systems now use
these actual Radiant types. They contain no native resource handles or MonoGame
conversions.

```csharp
using com.radiant.engine.graphics;

renderer.Configure(BlendState.AlphaBlend)
    .Configure(SamplerState.LinearClamp)
    .Configure(DepthStencilState.None)
    .Configure(RasterizerState.CullNone);

var erase = new BlendState
{
    ColorSourceBlend = Blend.Zero,
    ColorDestinationBlend = Blend.InverseSourceAlpha,
    AlphaSourceBlend = Blend.Zero,
    AlphaDestinationBlend = Blend.InverseSourceAlpha
};
var redOnly = BlendState.Opaque with { ColorWriteChannels = ColorWriteChannels.Red };
```

Descriptions are immutable records with init-only properties. Use an initializer
or `with` to create a variation; shared presets cannot be mutated. They are CPU
values, safe to retain across renderer lifetimes, and do not require disposal.
`AlphaBlend` expects premultiplied RGB; `NonPremultiplied` applies source alpha to
RGB. Constructors do not convert texture data between these representations.

The MonoGame adapter explicitly maps enums and rejects unknown values. Each renderer
owns a native-state cache keyed by description value, including custom states; equal
descriptions reuse native resources. Renderer disposal releases its cache. The shape
batcher owns a separate cache for its device lifetime. Retain a bounded set of
configurations: cache entries stay alive until their owner is disposed.

## Migration

- Replace imports of native render-state types with `com.radiant.engine.graphics`.
- Existing presets and object initializers retain their names.
- Replace assignments after construction with `with` expressions.
- Remove calls to `Dispose()` on state descriptions; the backend owns native resources.
- Sprite flipping uses Radiant `SpriteEffects`; sorting uses Radiant `SpriteSortMode`.
- `Configure(params object[])` rejects unsupported/native states instead of silently
  ignoring them. Texture/effect objects are not render-state descriptions.

This removes native render states from the public contract. Textures, effects,
render targets and the concrete renderer still need resource/backend abstractions;
this change does not provide a second graphics backend by itself. Future backends
must translate these descriptions and report unsupported features explicitly.

## Validation

The CPU suite scans public signatures for native states, compares all supplied
presets/defaults against MonoGame, checks enum mappings and invalid values, and
verifies cache reuse/disposal. The GPU regression checks alpha blending, a custom
erase blend, channel write masks, point/linear filtering and combined sprite flips:

```sh
dotnet run --project tests/Radiant.Tests -p:RadiantBuildContent=false
dotnet run --project tests/Radiant.3D -p:RadiantBuildContent=false -- --render-state-regression
```

Full 2D/3D pipeline regressions additionally cover depth, culling, post-processing
and ray tracing with the migrated descriptions.
