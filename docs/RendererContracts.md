# Backend contracts

Scenes and systems draw through `IRenderer` and control the window through `IWindow`. Both are available
wherever you write game code.

## The renderer

`Renderer` (an `IRenderer`) draws shapes, sprites, text and shader passes. Most calls return the renderer, so a
pass reads as one chain.

The following example shows how to draw a tinted badge in the corner of the screen:

```csharp
Renderer.Reset()
    .Configure(BlendState.AlphaBlend)
    .DrawTexture(Renderer.GetSolidTexture(Color.White), new Rectangle(16, 16, 120, 32), Color.Coral)
    .Commit();
```

Call renderer methods from render callbacks on the main thread. The only exception is
`DrawShapeParallel`, made for parallel queries. See the [renderer guide](Renderer.md) for everything it can do.

## The window

`Renderer.Window` lets you read and change the window.

The following example shows how to show the score in the title bar and quit on Escape:

```csharp
Renderer.Window.Title = $"My game — {score} points";

if (Controls.WasPressed(Key.Escape)) Renderer.Window.RequestClose();
```

`IsActive` tells you whether the window has focus, and `ClientBounds` gives its position and size in pixels.
`RequestClose` shuts the game down cleanly, disposing your scene first.

## Optional features

Some features depend on the GPU and on the optional Vulkan runtime. Ask before using them; each check also tells
you why a feature is missing.

The following example shows how to fall back when compute shaders aren't available:

```csharp
if (Renderer.SupportsComputeShaders(out string reason))
    kernel = Renderer.CreateComputeShader(spirv, 256, 256);
else
    Console.WriteLine($"Compute disabled: {reason}");
```

| Check | Feature |
| --- | --- |
| `SupportsComputeShaders` | [Compute shaders](ComputeShaders.md) |
| `SupportsPathTracing` | Hardware ray-traced path tracing |
| `SupportsDenoising` | NRD denoisers |
