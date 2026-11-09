using System;
using com.radiant.engine.runtime;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;

namespace com.radiant.engine.core;

/// <summary>Render-thread bridge to the optional MonoGame Vulkan ray-query runtime.</summary>
internal sealed class VulkanPathTraceSession : IPathTraceSession
{
    private const string Library = "mgruntime";

    internal IntPtr Context;
    internal MonoGameGraphicsBackend Backend { get; }

    public bool DenoisingSignals { get; }

    private readonly IRenderer Renderer;
    private ITraceGeometry UploadedSource;
    private long UploadedRevision = -1;

    public RenderTarget2D Sample { get; private set; }
    public int BuildCount { get; private set; }
    public float GpuMilliseconds => Context != IntPtr.Zero ? NativeGpuMilliseconds(Context) : -1;

    /// <summary>Sequentially laid-out frame parameters shared with the native rendering backend.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters
    {
        public Matrix InverseViewProjection;
        public Vector4 Environment;
        public uint Width, Height, Sequence, Bounces;
        public uint StablePrimary, HasCamera, Denoiser, Reserved1;
        public Vector4 ViewDepth;
    }

    /// <summary>Reads the ABI version exported by the optional native runtime.</summary>
    [DllImport(Library, EntryPoint = "RGE_RT_Version", CallingConvention = CallingConvention.Cdecl)] private static extern int Version();

    /// <summary>Returns the native backend's most recent error message pointer.</summary>
    [DllImport(Library, EntryPoint = "RGE_RT_Error", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr NativeError();

    /// <summary>Queries native runtime support on the supplied graphics device.</summary>
    [DllImport(Library, EntryPoint = "RGE_RT_Supported", CallingConvention = CallingConvention.Cdecl)] private static extern int Supported(IntPtr Device);

    /// <summary>Allocates a native backend session for the requested device and dimensions.</summary>
    [DllImport(Library, EntryPoint = "RGE_RT_Create", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Create(IntPtr Device, uint Width, uint Height, int Signals);

    /// <summary>Returns the native image handle for a backend output channel.</summary>
    [DllImport(Library, EntryPoint = "RGE_RT_Image", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Image(IntPtr Session);

    /// <summary>Uploads packed scene geometry into the native tracing session.</summary>
    [DllImport(Library, EntryPoint = "RGE_RT_Upload", CallingConvention = CallingConvention.Cdecl)] private static extern int Upload(IntPtr Session, [In] Vector4[] Triangles, uint Count);

    /// <summary>Records native GPU work for the supplied frame parameters.</summary>
    [DllImport(Library, EntryPoint = "RGE_RT_Dispatch", CallingConvention = CallingConvention.Cdecl)] private static extern int Dispatch(IntPtr Session, in Parameters Parameters);

    /// <summary>Reads the latest completed native tracing GPU duration.</summary>
    [DllImport(Library, EntryPoint = "RGE_RT_GpuMilliseconds", CallingConvention = CallingConvention.Cdecl)] private static extern float NativeGpuMilliseconds(IntPtr Session);

    /// <summary>Destroys a native backend session and its owned resources.</summary>
    [DllImport(Library, EntryPoint = "RGE_RT_Destroy", CallingConvention = CallingConvention.Cdecl)] private static extern int Destroy(IntPtr Session);

    /// <summary>Completes pending native work before resources are accessed or released.</summary>
    [DllImport(Library, EntryPoint = "RGE_RT_Flush", CallingConvention = CallingConvention.Cdecl)] private static extern int Flush(IntPtr Session);

    /// <summary>Converts the native backend's latest error into a managed message.</summary>
    private static Exception Error() => new InvalidOperationException(Marshal.PtrToStringUTF8(NativeError()) ?? "Vulkan path tracing failed.");

    /// <summary>Checks runtime and device capabilities and reports an unavailability reason.</summary>
    public static bool IsSupported(NativeGraphicsHandles Handles, out string Reason)
    {
        if (Handles.Backend != GraphicsBackend.Vulkan)
        {
            Reason = "Vulkan path tracing requires the Vulkan graphics backend.";
            return false;
        }

        try
        {
            if (Version() != 2) { Reason = "Vulkan RT runtime ABI mismatch; rebuild the optional runtime."; return false; }
            if (Supported(Handles.LogicalDevice) == 0) { Reason = "This Vulkan device does not enable hardware ray queries."; return false; }
        }
        catch (Exception Exception) when (Exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Reason = "Optional Vulkan RT runtime is not installed; using shader BVH traversal.";
            return false;
        }

        Reason = "Vulkan KHR ray queries; shared GPU texture";

        return true;
    }

    /// <summary>Creates a native tracing session and wraps its output images.</summary>
    public VulkanPathTraceSession(IRenderer Renderer, MonoGameGraphicsBackend Backend, NativeGraphicsHandles Handles, int Width, int Height, bool Signals)
    {
        this.Renderer = Renderer;
        this.Backend = Backend;
        DenoisingSignals = Signals;
        Context = Create(Handles.LogicalDevice, checked((uint)Width), checked((uint)Height), Signals ? 1 : 0);

        if (Context == IntPtr.Zero) throw Error();

        try { Sample = Backend.WrapImage(Image(Context), Width, Height); }
        catch { Destroy(Context); Context = IntPtr.Zero; throw; }
    }

    /// <summary>Uploads packed scene geometry into the native tracing session.</summary>
    public void Upload(ITraceGeometry Geometry)
    {
        if (ReferenceEquals(UploadedSource, Geometry) && UploadedRevision == Geometry.Revision) return;

        var triangles = Geometry.Triangles;
        var packed = new Vector4[checked(triangles.Count * 5)];

        for (int index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            packed[index * 5] = new(triangle.A, 0);
            packed[index * 5 + 1] = new(triangle.B, 0);
            packed[index * 5 + 2] = new(triangle.C, 0);
            packed[index * 5 + 3] = new(triangle.Albedo, 0);
            packed[index * 5 + 4] = new(triangle.Emission, 0);
        }

        if (Upload(Context, packed, checked((uint)triangles.Count)) == 0) throw Error();

        UploadedSource = Geometry;
        UploadedRevision = Geometry.Revision;
        BuildCount++;
    }

    /// <summary>Dispatches one tracing frame with the supplied camera and transport settings.</summary>
    public void Trace(ITraceGeometry Geometry, Vector3 Environment, int Sequence, int Bounces, bool StablePrimary, int Denoiser)
    {
        var parameters = new Parameters
        {
            InverseViewProjection = Geometry.Camera.InverseViewProjection,
            Environment = new(Environment, 0), Width = (uint)Sample.Width, Height = (uint)Sample.Height,
            Sequence = (uint)Sequence, Bounces = (uint)Bounces,
            StablePrimary = StablePrimary ? 1u : 0u, HasCamera = Geometry.HasCamera ? 1u : 0u,
            Denoiser = (uint)Denoiser, ViewDepth = new(Geometry.Camera.View.M13, Geometry.Camera.View.M23, Geometry.Camera.View.M33, Geometry.Camera.View.M43)
        };

        if (Dispatch(Context, in parameters) == 0) throw Error();
    }

    /// <summary>Releases the native tracing session and its managed image wrappers.</summary>
    public void Dispose()
    {
        if (Context == IntPtr.Zero) return;

        Renderer.SetTarget(null);

        // MonoGame destroys external views immediately. Submit recorded consumers first.
        if (Flush(Context) == 0) throw Error();

        Sample?.Dispose();
        Sample = null;

        if (Destroy(Context) == 0) throw Error();

        Context = IntPtr.Zero;
    }
}
