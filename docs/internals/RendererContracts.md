# Renderer and window contracts

Scenes, ECS instances and systems now expose `IRenderer` from
`com.radiant.engine.core`. The existing MonoGame renderer implements it. Fluent
commands return `IRenderer`, so chaining cannot expose a concrete/native renderer.
Resource creation is inherited from `IGraphicsResourceFactory`; all command inputs
use Radiant or .NET types. Text drawing uses font names and sizes, without returning
FontStashSharp objects through the interface.

```csharp
public void DrawOverlay(IRenderer renderer)
{
    renderer.Reset()
        .Configure(BlendState.AlphaBlend)
        .DrawTexture(renderer.GetSolidTexture(Color.White),
            new Rectangle(16, 16, 120, 32), Color.Coral)
        .Commit();
}
```

`IRasterGeometry.Draw` and denoising frames also use `IRenderer`, allowing shared
pipelines to submit commands to another implementation. The interface currently
covers the full existing renderer service, including fitted views, shape collection,
resource caches and presentation. Implementations must preserve command order,
return themselves from fluent methods, honor the resource ownership rules in
[GraphicsResources.md](GraphicsResources.md), and release their resources on disposal.
Commands run on the render thread, except explicitly parallel shape collection.

## Window and host

`renderer.Window` is an `IWindow` (`com.radiant.engine.runtime`) with title, focus,
client bounds and `RequestClose()`. A close request goes through normal host shutdown.
Inspector exit actions use this contract. There is no native window handle, content
manager, device or inherited `Game` API on it.

A runtime backend assigns its `IRenderer` in `IRuntimeHost.PrepareScene`, before scene
and ECS initialization. `BeginFrame`/`EndFrame` drive presentation; `ReleaseScene`
disposes the renderer after the scene's systems. The existing headless host still
provides no renderer and skips rendering. Tests additionally provide a recording
renderer through another host and execute ECS drawing without initializing MonoGame.
See [RuntimeBackends.md](RuntimeBackends.md) for host lifecycle rules.

## Optional features

`IRenderer` inherits `IComputeRenderer`. Backends without compute inherit a false
capability result with a reason, and allocation throws `NotSupportedException`.
`CreateComputeShader` returns `IComputeShader`; its output is a session-owned Radiant
texture. The current Vulkan implementation remains internal and retains its SPIR-V,
threading and synchronization rules. Check support before allocating.

Vulkan tracing and NRD support queries also default to false. Native tracing session
creation is an internal optional backend interface, so software tracing and ordinary
drawing do not require native Vulkan session types in public contracts.

## Migration and remaining work

Change explicit `Renderer` parameters/fields in game code to `IRenderer`, explicit
`ComputeShader` variables to `IComputeShader`, and `renderer.Window.Exit()` to
`renderer.Window.RequestClose()`. Source using inferred renderer variables and fluent
commands generally needs no changes. Pacman and Zuma consume the interface now.

The desktop implementation still uses MonoGame. Its legacy concrete `Renderer` and
`Window` construction APIs remain available for backend integration and native test
harnesses; deprecated device/SpriteBatch escape hatches are excluded from `IRenderer`.
This change does not implement a second GPU backend or remove the package dependency.
Common view calculations and resource caching are now separated as described below.
GPU command execution, glyph rasterization and shape submission remain in backend adapters.
Font caching/metrics and CPU shape collection are separated as described below.
A second command backend can reuse the extracted services without changing scene/system dependencies.

## Shared view and asset services

`RenderView` owns viewport metrics, virtual resolution, proportional `FitWorld`,
world/screen conversion, pixel-edge snapping, the world/clip matrices and dynamic
resolution scale. It uses only Radiant and .NET types and can be constructed without
a GPU or window. A backend calls `Resize(width, height)` with presentation dimensions,
then applies `ViewProjection` to its shape batches. The current renderer delegates
its existing view API to this service, so game call sites remain unchanged.

All size and scale metrics are updated before scale-change callbacks. Render scale
is clamped to 0.25–1, rejects nonfinite inputs, and only notifies when the effective
value changes beyond the existing 0.001 tolerance. It changes target resolution,
not input coordinates. `Resize` rejects nonpositive dimensions and dimensions whose
area/power-of-two metrics cannot be represented; invalid inputs preserve the last
valid view. The desktop adapter retains that view while the presentation size is zero.

`IGraphicsAssetLoader` separates backend content loading from shared texture and
shader caches. Each load returns a disposable Radiant handle; native assets may be
shared, but releasing a handle must not invalidate another consumer. Effects must
have independent parameters. The MonoGame loader borrows content textures and clones
effects; the common caches use this interface and `IGraphicsResourceFactory` and
contain no MonoGame types. Procedural solid/circle/triangle/rounded textures are
also generated through the resource factory.

CPU regressions exercise views and caches with an in-memory resource backend and no
graphics device. Game graphics checks verify that sprites, pointer mapping and scaled
metrics stay consistent after resizing. The camera inspector CPU fixture now uses
`IRenderer` and `RenderView` instead of an uninitialized native renderer.

## Fonts and shape batches

`FontFace`, `IFontFamily` and `IFontLoader` live in `com.radiant.engine.graphics`.
`GetFont(name, size)` now returns a Radiant font face with exact-size measurement and
line height, including through `IRenderer`. Faces are borrowed from their family;
disposing the renderer invalidates them. Do not dispose a face independently.
`MeasureString`, `GetLineHeight`, named loading and supersampling remain common
services. The current adapter owns FontStashSharp families, file loading and glyph
drawing; public font signatures no longer expose FontStashSharp. Scales/sizes must
be positive and finite. Failed native font loads dispose their temporary family.

`ShapeCollection` is a CPU-only service for serial and parallel accumulation. Each
worker owns one slot; initialize/grow shared storage and merge only after workers
finish. After direct buffer writes, set the worker count and call `SortBufferByZ`
before `CollectParallelSorted`. Shapes and Z values are sorted together. Equal-depth
ordering is unspecified. `Items` is a read-only view of the populated main buffer;
consume it before mutating or growing the collection. Reacquire worker arrays after
a growth operation.

`IShapeBatchBackend.Submit` receives a populated shape array, count, projection,
target and draw settings. It must consume the array synchronously without retaining
it. Zero-count submissions still bind/clear the target. The MonoGame adapter owns
vertex declarations, GPU buffer growth and instanced draws. GPU and CPU capacities
are independent, so a large external submission cannot invalidate later accumulated
batches. Collection counts clear only after successful submission. Timing properties
measure CPU upload/submission calls, not GPU execution time.

CPU tests cover parallel growth, count validation, three-cycle Z ordering and merges,
and font caching/metrics with a simulated family. GPU tests cover more than 65,536
external shapes followed by an accumulated batch, sorted overlaps, empty clears,
font lifetime, and existing sprite/state pixels. Game graphics checks also verify
HUD font metrics and cache identity. Sprite command execution still uses the current MonoGame renderer; a second GPU
backend is not implemented yet.

## Triangle and mesh submission

`IGeometryBackend` receives Radiant geometry, effects and immutable render-state
settings. Calls run synchronously on the render thread against the current target;
the renderer flushes pending sprites first to preserve command order. Effects must
belong to the backend/device performing the draw. Empty triangle arrays are a no-op;
nonempty arrays must contain complete triangles and must not be retained by a backend.

`Mesh.VertexData` and `Mesh.IndexData` expose read-only spans over the mesh's immutable
constructor snapshot. Another backend can upload these without native vertex types.
The MonoGame adapter owns its vertex declarations, dynamic triangle buffer and cached
mesh uploads. Failed mesh uploads dispose partial allocations; growing the triangle
buffer allocates its replacement before disposing the old buffer. Disposing the
adapter releases all cached uploads and render states. Existing `DrawMesh` and
`DrawTriangles` game calls remain unchanged.
