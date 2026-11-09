# Radiant graphics resources

Textures, render targets, effects and their format enums live in
`com.radiant.engine.graphics`. Public materials, pipeline outputs, denoising inputs,
sprite commands and compute outputs use these types. They have no native base class,
public native handle or implicit MonoGame conversion.

```csharp
using com.radiant.engine.graphics;
using com.radiant.engine.math;
using System.Numerics;

using Texture2D source = renderer.CreateTexture(2, 2);
source.SetData(new[] { Color.Red, Color.White, Color.Lime, Color.Black });
using RenderTarget2D output = renderer.CreateRenderTarget(256, 256,
    SurfaceFormat.Color, usage: RenderTargetUsage.PreserveContents);
renderer.Reset().SetTarget(output).DrawTexture(source, output.Bounds).Commit();
renderer.SetTarget(null);
```

## Contracts and ownership

`IGraphicsResource` exposes disposal and lifetime. `Texture2D`, `RenderTarget2D`,
`Effect`, their parameters, techniques and passes are abstract contracts that another
backend can implement. `IGraphicsResourceFactory` creates images and queries supported
sample counts. `Renderer` implements that interface and delegates allocation to its
internal MonoGame factory. Asset loading is injected through `IGraphicsAssetLoader`,
while shared caches retain resource identity and ownership independently of that loader. Scenes also consume [IRenderer and IWindow](RendererContracts.md); their current
implementations still use MonoGame internally. A second graphics backend and a separate core
package without MonoGame dependencies are not implemented yet.

- `CreateTexture` and `CreateRenderTarget` return caller-owned resources. Dispose them
  on the render thread before disposing the renderer. Unbind active targets first.
- `GetTexture`, solid/shape helpers and `GetShaderEffect` return shared cached resources.
  Do not dispose them individually. The renderer releases its caches; the content manager
  owns loaded asset images. Repeated lookups return the same Radiant object.
- Use `ReleaseShader(name)` to release a cached effect. Loading it again creates a fresh
  instance, including fresh parameters and techniques. Retained parameters/passes reject
  use after disposal. Techniques cannot be assigned to a different effect.
- `IComputeShader.Output` belongs to the compute session. Sample it or read it back after
  synchronization; do not dispose it independently. Its native image ownership is unchanged.

The native adapter rejects disposed textures and resources from another backend/device
before binding. Format conversions are explicit and reject unknown enum values. Radiant
currently exposes uncompressed Color/Alpha8, Single/Vector2/Vector4 and
HalfSingle/HalfVector2/HalfVector4 formats; backend support still depends on the device.
Depth and sample properties report the actual backend-selected target configuration.

## Data and effects

`GetData` and `SetData` accept struct arrays and optional Radiant rectangles. Pixel layout
must match the texture format (for example Radiant `Color` for Color, `Vector4` for
Vector4); region operations also accept a mip level and array offset/count. Readback may
require GPU submission/synchronization. `SaveAsPng` retains the backend's format support.

Use fluent `SetParameter` or `GetShaderEffect(name).Parameters[name].SetValue(...)`.
Scalar/vector/matrix inputs use .NET types; texture inputs use Radiant textures. The
backend retains conversion buffers for vector/matrix arrays. Missing optional parameter
names return null. Techniques and pass collections are read-only.

Existing applications should replace native resource and format imports with
`com.radiant.engine.graphics`, create resources through `Renderer`, and replace native
readback rectangles with Radiant rectangles. Bind MRTs with `SetTargets(targetA, targetB)`;
the native `RenderTargetBinding[]` overload is internal.

## Validation

The CPU suite scans exported signatures for native resource leaks and tests format
mapping/invalid values. `--render-state-regression` additionally checks texture regions,
cache identity, effect release/reload, technique ownership, disposed-resource rejection
and rendered pixels. The 2D/3D pipeline and compute regressions exercise these contracts
through real rendering. Sibling Pacman and Zuma use the same API; build all three with
`python eng/check-consumers.py` and run graphical checks on isolated displays.
