# Compute shaders

Run your own GPU compute kernels on Vulkan and draw their results like any other texture, with no CPU round
trip.

## Enable compute

Compute needs the optional Vulkan runtime. Build it once, then build your game with it:

```sh
python eng/build-vulkan-rt.py
dotnet build MyGame -p:RadiantVulkanRT=true
```

The build needs Python 3.12+, CMake, a C/C++ compiler and `glslangValidator` (part of the Vulkan SDK). Check at
runtime with `Renderer.SupportsComputeShaders(out string reason)`.

## Write a kernel

Write GLSL and compile it to SPIR-V:

```sh
python eng/compile-compute.py shaders/Fill.comp artifacts/compute/Fill.spv
```

Your kernel follows a few rules:

- The entry point is `main`.
- It writes one `rgba32f image2D` at set 0, binding 0. That image becomes the kernel's `Output` texture.
- Optional push constants must match the byte count you pass when creating the kernel (a multiple of four). Lay
  them out explicitly; a C# `Vector3` doesn't match GLSL padding.
- Declare the local group size in the shader and skip pixels outside the image.
- Write every pixel before reading it; new images start with undefined contents.

## Dispatch and draw

The following example shows how to create a kernel once, fill it with a color each frame and draw the result:

```csharp
// In Initialize:
kernel = Renderer.CreateComputeShader(File.ReadAllBytes("Fill.spv"), 65, 49, 16); // 65 × 49 pixels, 16 bytes of constants

// In Render:
Vector4 color = new(0.2f, 0.4f, 0.8f, 1);
kernel.Dispatch(9, 7, Constants: MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref color, 1)));
Renderer.DrawTexture(kernel.Output, Vector2.Zero).Commit();
```

`Dispatch` takes workgroup counts. Consecutive dispatches can read and update the same image; synchronization is
automatic.

## Read results on the CPU

Call `kernel.Synchronize()` before `kernel.Output.GetData(...)`. Drawing the output needs no synchronization.

## Keep it healthy

- Create, dispatch and dispose kernels from render callbacks.
- Dispose the kernel in your system's `Dispose`; never dispose `Output` yourself or draw into it.
- Recreate the kernel when its output size should change.
