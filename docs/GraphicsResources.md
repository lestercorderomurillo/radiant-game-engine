# Resources and textures

Create textures and render targets through the renderer, fill them with pixels or draw into them, and use them
anywhere a texture is accepted.

## Create and fill a texture

The following example shows how to build a tiny checkerboard and draw it scaled up into a render target:

```csharp
using com.radiant.engine.graphics;
using com.radiant.engine.math;

using Texture2D checker = Renderer.CreateTexture(2, 2);
checker.SetData(new[] { Color.Red, Color.White, Color.White, Color.Red });

using RenderTarget2D output = Renderer.CreateRenderTarget(256, 256, usage: RenderTargetUsage.PreserveContents);
Renderer.Reset().Configure(SamplerState.PointClamp).SetTarget(output).DrawTexture(checker, output.Bounds).Commit();
Renderer.SetTarget(null);
```

## Keep a target's contents

Targets are cleared when you switch away from them unless you create them with
`RenderTargetUsage.PreserveContents`. Use it for anything you read in a later frame, such as history buffers.

## Choose a format

| Format | Good for |
| --- | --- |
| `Color` | Regular images, 8 bits per channel |
| `HalfVector4`, `Vector4` | HDR lighting and accumulation |
| `HalfVector2`, `Vector2` | Motion vectors, distance fields |
| `HalfSingle`, `Single` | Depth and other single values |
| `Alpha8` | Masks |

The following example shows an HDR lighting target:

```csharp
var lighting = Renderer.CreateRenderTarget(Renderer.ScaledWidth, Renderer.ScaledHeight, SurfaceFormat.HalfVector4);
```

## Read pixels back

`GetData` and `SetData` work with whole textures or regions. Use an array type that matches the format:
`Color[]` for `Color`, `Vector4[]` for `Vector4`. Reading back waits for the GPU, so avoid it every frame.

## Know who disposes what

- Textures and targets from `CreateTexture` and `CreateRenderTarget` are yours: dispose them in your system's
  `Dispose`, and unbind a target before disposing it.
- Textures from `GetTexture`, `GetSolidTexture` and the other `Get…` helpers are shared and cached. Never dispose
  them; the renderer does.
- Shaders from `GetShaderEffect` are cached too. Call `ReleaseShader(name)` to unload one.
- A compute shader's `Output` belongs to the compute shader.

## Set shader parameters

The fluent `SetParameter` works on the active shader. To prepare a shader ahead of time, set its parameters
directly:

```csharp
var bloom = Renderer.GetShaderEffect("Effects/Bloom");
bloom.Parameters["Threshold"].SetValue(0.8f);
```

A missing parameter name returns null, which lets one system feed several shader variants.
