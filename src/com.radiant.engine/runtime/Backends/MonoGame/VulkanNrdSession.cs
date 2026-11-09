using System;
using com.radiant.engine.runtime;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;

namespace com.radiant.engine.core;

/// <summary>Per-view native NRD resources. Uses the graphics frame command buffer and shared GPU images.</summary>
internal sealed class VulkanNrdSession : ITraceDenoiserSession
{
    private const string Library = "mgruntime";
    private IntPtr Context;
    private readonly IRenderer Renderer;
    private readonly MonoGameGraphicsBackend Backend;

    public RenderTarget2D Output { get; private set; }

    /// <summary>Sequentially laid-out frame parameters shared with the native rendering backend.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters { public Matrix View, Projection; public uint Reset; }

    /// <summary>Queries native runtime support on the supplied graphics device.</summary>
    [DllImport(Library, EntryPoint = "RGE_NRD_Supported", CallingConvention = CallingConvention.Cdecl)] private static extern int Supported(IntPtr Device);

    /// <summary>Allocates a native backend session for the requested device and dimensions.</summary>
    [DllImport(Library, EntryPoint = "RGE_NRD_Create", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Create(IntPtr Trace, uint Mode);

    /// <summary>Returns the native image handle for a backend output channel.</summary>
    [DllImport(Library, EntryPoint = "RGE_NRD_Image", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Image(IntPtr Session);

    /// <summary>Records native GPU work for the supplied frame parameters.</summary>
    [DllImport(Library, EntryPoint = "RGE_NRD_Dispatch", CallingConvention = CallingConvention.Cdecl)] private static extern int Dispatch(IntPtr Session, IntPtr Trace, in Parameters Parameters);

    /// <summary>Completes pending native work before resources are accessed or released.</summary>
    [DllImport(Library, EntryPoint = "RGE_NRD_Flush", CallingConvention = CallingConvention.Cdecl)] private static extern int Flush(IntPtr Session);

    /// <summary>Destroys a native backend session and its owned resources.</summary>
    [DllImport(Library, EntryPoint = "RGE_NRD_Destroy", CallingConvention = CallingConvention.Cdecl)] private static extern int Destroy(IntPtr Session);

    /// <summary>Returns the native backend's most recent error message pointer.</summary>
    [DllImport(Library, EntryPoint = "RGE_RT_Error", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr NativeError();

    /// <summary>Converts the native backend's latest error into a managed message.</summary>
    private static Exception Error() => new InvalidOperationException(Marshal.PtrToStringUTF8(NativeError()) ?? "NRD failed.");

    /// <summary>Checks runtime and device capabilities and reports an unavailability reason.</summary>
    public static bool IsSupported(NativeGraphicsHandles Handles, out string Reason)
    {
        if (!VulkanPathTraceSession.IsSupported(Handles, out Reason)) return false;

        try
        {
            if (Supported(Handles.LogicalDevice) == 0)
            {
                Reason = "NRD requires the --nrd native runtime build and storage-image read/write support.";
                return false;
            }
        }
        catch (Exception Exception) when (Exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Reason = "Build the optional NRD runtime: python3 eng/build-vulkan-rt.py --nrd";
            return false;
        }

        Reason = "NRD diffuse reconstruction on Vulkan; shared GPU images";

        return true;
    }

    /// <summary>Creates an NRD session associated with the supplied native tracer.</summary>
    public VulkanNrdSession(IRenderer Renderer, MonoGameGraphicsBackend Backend, VulkanPathTraceSession Trace, int Mode)
    {
        this.Renderer = Renderer;
        this.Backend = Backend;
        Context = Create(Trace.Context, (uint)Mode);

        if (Context == IntPtr.Zero) throw Error();

        try { Output = Backend.WrapImage(Image(Context), Trace.Sample.Width, Trace.Sample.Height); }
        catch { Destroy(Context); Context = IntPtr.Zero; throw; }
    }

    /// <summary>Runs native denoising with the current camera matrices and reset state.</summary>
    public void Render(IPathTraceSession trace, TraceCamera Camera, bool Reset)
    {
        if (trace is not VulkanPathTraceSession Trace || !ReferenceEquals(Trace.Backend, Backend))
            throw new ArgumentException("Tracing session belongs to a different backend.", nameof(trace));
        Renderer.SetTarget(null);

        var parameters = new Parameters { View = Camera.View, Projection = Camera.Projection, Reset = Reset ? 1u : 0u };

        if (Dispatch(Context, Trace.Context, in parameters) == 0) throw Error();
    }

    /// <summary>Releases the native denoising session and its managed image wrappers.</summary>
    public void Dispose()
    {
        if (Context == IntPtr.Zero) return;

        Renderer.SetTarget(null);

        if (Flush(Context) == 0) throw Error();

        Output?.Dispose();
        Output = null;

        if (Destroy(Context) == 0) throw Error();

        Context = IntPtr.Zero;
    }
}
