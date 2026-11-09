using System;
using Microsoft.Xna.Framework.Graphics;

#pragma warning disable CS0618
namespace com.radiant.engine.core;

public partial class Renderer
{
    /// <summary>Checks the active device and optional native runtime, without allocating tracing resources.</summary>
    public bool SupportsVulkanPathTracing(out string Reason) => VulkanPathTraceSession.IsSupported(Device.GetNativeHandles(), out Reason);

    /// <summary>Creates a native tracing session through the renderer's active graphics device.</summary>
    internal VulkanPathTraceSession CreateVulkanPathTracer(int Width, int Height, bool Signals = false)
    {
        CommitTextures();

        if (!SupportsVulkanPathTracing(out var reason)) throw new NotSupportedException(reason);

        return new(this, Device.GetNativeHandles(), Width, Height, Signals);
    }

    /// <summary>Checks the optional NRD runtime and active Vulkan device.</summary>
    public bool SupportsNrdDenoising(out string Reason) => VulkanNrdSession.IsSupported(Device.GetNativeHandles(), out Reason);

    /// <summary>Creates a native NRD session associated with a Vulkan tracing session.</summary>
    internal VulkanNrdSession CreateVulkanDenoiser(VulkanPathTraceSession Trace, int Mode)
    {
        CommitTextures();

        if (!SupportsNrdDenoising(out var reason)) throw new NotSupportedException(reason);

        return new(this, Trace, Mode);
    }

    /// <summary>Wraps a native Vulkan image for use by managed renderer stages.</summary>
    internal RenderTarget2D WrapVulkanTraceImage(IntPtr Image, int Width, int Height) =>
        RenderTarget2D.FromNativeHandle(Device, Image, Width, Height, SurfaceFormat.Vector4, DepthFormat.None, 0, false);
}
