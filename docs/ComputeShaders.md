# Compute shaders (Vulkan)

Radiant exposes `Renderer.SupportsComputeShaders(out reason)` and
`Renderer.CreateComputeShader(spirv, width, height, pushConstantBytes)` through its
optional patched MonoGame Vulkan runtime. The managed MonoGame API/packages remain
unchanged. This is a local, reproducible native extension, not a published GitHub fork.

## Build and run

Prerequisites are the same as `eng/build-vulkan-rt.py`, including glslangValidator.
From the repository root:

```sh
python eng/build-vulkan-rt.py
python eng/compile-compute.py tests/Shared/Compute/Fill.comp artifacts/compute/Fill.spv
dotnet build tests/Radiant.3D -p:RadiantVulkanRT=true
dotnet tests/Radiant.3D/bin/Debug/net8.0/Radiant.3D.dll --compute-regression
```

Use `--nrd` on the native build command if NRD is also needed. Build each executable
with `-p:RadiantVulkanRT=true` to copy the extension into its output; ordinary builds
restore the stock runtime. No hardware ray-query capability is required for compute.
The integration currently targets Linux and Windows; only Linux has been tested.

## Kernel contract

- SPIR-V compute entry point `main`; compile GLSL with the helper above. MGFX effect
  bytecode is not accepted. HLSL needs a separate SPIR-V compiler and the same layout.
- One read/write `rgba32f image2D`, descriptor set 0, binding 0. The kernel owns this
  texture and exposes it as `Texture2D Output` for subsequent graphics passes.
- Optional push constants: exact byte count, multiple of four, within device limits.
  Match the shader's layout explicitly; a C# Vector3 is not automatically std430-safe.
- `Dispatch(x, y, z)` takes workgroup counts. Local sizes are declared in the kernel
  and must satisfy the GPU limits. Bound-check partial groups in the kernel.
- Newly allocated image contents are undefined. Initialize every pixel before reading
  it. Consecutive dispatches can read and update this image; barriers are automatic.
- This first API does **not** expose structured buffers, arbitrary input textures,
  multiple output images, indirect dispatch, or the cpt-max Effect API. It does not
  convert existing rendering passes to compute automatically.

Example, using the supplied Fill kernel (16 bytes of push constants):

```csharp
// Create on the render thread; retain between frames.
var kernel = renderer.CreateComputeShader(File.ReadAllBytes("Fill.spv"), 65, 49, 16);

// During Draw:
Vector4 color = new(0.2f, 0.4f, 0.8f, 1);
kernel.Dispatch(9, 7, Constants:
    MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref color, 1)));
renderer.DrawTexture(kernel.Output, Vector2.Zero).Commit();
```

Use the rendering thread for creation, dispatch, synchronization and disposal.
Dispose the kernel before disposing its renderer/device. Do not dispose `Output`
separately, use it as a render target, or mutate it with SetData. Recreate the kernel
when output size or device changes. Do not leave an independently managed SpriteBatch
open across dispatches; the API commits only the owning Renderer's pending batch.

GPU rendering consumes `Output` without CPU readback or a per-dispatch wait.
For explicit CPU readback, call `kernel.Synchronize()` before `GetData`; MonoGame
cannot infer pending writes performed by this extension. Allocation/disposal may
wait for the device. Use trusted, validated shader modules matching this contract;
the header check is not full SPIR-V reflection or validation.

## Research and version choice (2026-11-08)

The latest stable release listed upstream is
[MonoGame 3.8.6.1](https://github.com/MonoGame/MonoGame/releases).
[PR #7533](https://github.com/MonoGame/MonoGame/pull/7533) adds compute to DirectX and
OpenGL; it is not an implementation for Radiant's native Vulkan backend. Copying
that branch would not supply Vulkan compute.

This change therefore extends the existing native integration pinned to MonoGame
3.8.6, commit `c610a529667336f63da323c6057274258727664b`. It deliberately leaves the
managed packages, shader tools and native ABI on the same existing version. Updating
the pin to 3.8.6.1 requires a separate validation of the existing RT/NRD patches;
this change does not claim that upgrade or port code from cpt-max.

## Validation

The GPU regression verifies all 65×49 pixels after two dispatches (write, then
read/modify/write), alternates SpriteBatch and fullscreen composition, changes push
constants over eight frames, and destroys/recreates the kernel. CPU verification
happens only after composition. It also checks invalid group counts, constant sizes
and use after disposal. Tested on AMD Radeon RX 9070 XT with RADV.

Stock runtime behavior can be checked with:

```sh
dotnet build tests/Radiant.3D -p:RadiantVulkanRT=false
dotnet tests/Radiant.3D/bin/Debug/net8.0/Radiant.3D.dll --compute-regression --expect-unavailable
```

Windows execution, validation-layer runs, and GPUs without ray tracing remain untested.
