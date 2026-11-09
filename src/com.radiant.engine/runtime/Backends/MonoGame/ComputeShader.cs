using System;
using com.radiant.engine.runtime;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;

namespace com.radiant.engine.core;

/// <summary>
/// A Vulkan compute kernel with one owned read/write RGBA32F image at set 0, binding 0.
/// Uses SPIR-V entry point main and optional push constants. Create, dispatch and dispose
/// on the render thread. Dispose before the renderer; do not dispose or render into Output.
/// </summary>
internal sealed class ComputeShader : IComputeShader
{
    private const string Library = "mgruntime";
    private readonly IRenderer Renderer;
    private readonly int Thread = Environment.CurrentManagedThreadId;
    private readonly int ConstantBytes;
    private IntPtr Context;
    /// <summary>GPU output for sampling by subsequent render passes, without CPU readback.</summary>
    public Texture2D Output { get; private set; }

    [DllImport(Library, EntryPoint = "RGE_Compute_Version", CallingConvention = CallingConvention.Cdecl)] private static extern int Version();
    [DllImport(Library, EntryPoint = "RGE_Compute_Supported", CallingConvention = CallingConvention.Cdecl)] private static extern int Supported(IntPtr Device);
    [DllImport(Library, EntryPoint = "RGE_Compute_Create", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Create(IntPtr Device, [In] uint[] Code, uint Bytes, uint Width, uint Height, uint Constants);
    [DllImport(Library, EntryPoint = "RGE_Compute_Image", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Image(IntPtr Session);
    [DllImport(Library, EntryPoint = "RGE_Compute_Dispatch", CallingConvention = CallingConvention.Cdecl)] private static extern unsafe int NativeDispatch(IntPtr Session, uint X, uint Y, uint Z, byte* Constants);
    [DllImport(Library, EntryPoint = "RGE_Compute_Flush", CallingConvention = CallingConvention.Cdecl)] private static extern int Flush(IntPtr Session);
    [DllImport(Library, EntryPoint = "RGE_Compute_Destroy", CallingConvention = CallingConvention.Cdecl)] private static extern int Destroy(IntPtr Session);
    [DllImport(Library, EntryPoint = "RGE_RT_Error", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr NativeError();
    private static Exception Error() => new InvalidOperationException(Marshal.PtrToStringUTF8(NativeError()) ?? "Vulkan compute failed.");

    internal static bool IsSupported(NativeGraphicsHandles Handles, out string Reason)
    {
        Reason = "Compute requires the Radiant Vulkan runtime and a compute-capable graphics queue.";
        if (Handles.Backend != GraphicsBackend.Vulkan) return false;
        try
        {
            if (Version() != 1 || Supported(Handles.LogicalDevice) == 0) return false;
        }
        catch (Exception Exception) when (Exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return false; }
        Reason = "Vulkan compute with shared RGBA32F output; hardware ray tracing is not required.";
        return true;
    }

    internal ComputeShader(IRenderer Renderer, MonoGameGraphicsBackend Backend, NativeGraphicsHandles Handles, ReadOnlySpan<byte> Spirv, int Width, int Height, int PushConstantBytes)
    {
        if (Width <= 0 || Height <= 0) throw new ArgumentOutOfRangeException(nameof(Width), "Both dimensions must be positive.");
        if (PushConstantBytes < 0 || PushConstantBytes % 4 != 0) throw new ArgumentOutOfRangeException(nameof(PushConstantBytes));
        if (Spirv.Length < 20 || Spirv.Length % 4 != 0 || global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(Spirv) != 0x07230203)
            throw new ArgumentException("Expected a SPIR-V module, not MGFX bytecode.", nameof(Spirv));
        this.Renderer = Renderer;
        ConstantBytes = PushConstantBytes;
        // uint[] guarantees the alignment required by VkShaderModuleCreateInfo.
        var code = MemoryMarshal.Cast<byte, uint>(Spirv).ToArray();
        Context = Create(Handles.LogicalDevice, code, (uint)Spirv.Length, (uint)Width, (uint)Height, (uint)PushConstantBytes);
        if (Context == IntPtr.Zero) throw Error();
        try { Output = Backend.WrapImage(Image(Context), Width, Height); }
        catch { Destroy(Context); Context = IntPtr.Zero; throw; }
    }

    /// <summary>
    /// Records workgroup counts (not pixel counts) during Draw. Constants must match the
    /// declared size and shader layout. Kernels must bound-check partial workgroups.
    /// Output contents are undefined until written by the kernel.
    /// </summary>
    public unsafe void Dispatch(int GroupsX, int GroupsY, int GroupsZ = 1, ReadOnlySpan<byte> Constants = default)
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(Context == IntPtr.Zero, this);
        if (GroupsX <= 0 || GroupsY <= 0 || GroupsZ <= 0) throw new ArgumentOutOfRangeException(nameof(GroupsX));
        if (Constants.Length != ConstantBytes) throw new ArgumentException("Push constant size does not match the pipeline.", nameof(Constants));
        Renderer.Commit();
        fixed (byte* data = Constants)
            if (NativeDispatch(Context, (uint)GroupsX, (uint)GroupsY, (uint)GroupsZ, data) == 0) throw Error();
    }

    /// <summary>Waits for GPU work before explicit CPU readback. Normal rendering does not need this.</summary>
    public void Synchronize()
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(Context == IntPtr.Zero, this);
        Renderer.Commit();
        if (Flush(Context) == 0) throw Error();
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != Thread) throw new InvalidOperationException("Compute resources must be used on their creation/render thread.");
    }

    /// <summary>Waits for queued consumers and releases the owned image and kernel.</summary>
    public void Dispose()
    {
        CheckThread();
        if (Context == IntPtr.Zero) return;
        Renderer.SetTarget(null);
        if (Flush(Context) == 0) throw Error();
        Output?.Dispose();
        Output = null;
        if (Destroy(Context) == 0) throw Error();
        Context = IntPtr.Zero;
    }
}
