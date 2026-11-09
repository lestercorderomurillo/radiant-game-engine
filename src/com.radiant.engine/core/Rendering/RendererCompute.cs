using System;

#pragma warning disable CS0618
namespace com.radiant.engine.core;

public partial class Renderer
{
    /// <summary>Checks general compute availability independently of ray tracing support.</summary>
    public bool SupportsComputeShaders(out string Reason) => ComputeShader.IsSupported(Device.GetNativeHandles(), out Reason);

    /// <summary>Creates a SPIR-V compute kernel with one read/write RGBA32F output texture.</summary>
    public ComputeShader CreateComputeShader(ReadOnlySpan<byte> Spirv, int Width, int Height, int PushConstantBytes = 0)
    {
        if (!SupportsComputeShaders(out var reason)) throw new NotSupportedException(reason);
        CommitTextures();
        return new(this, Device.GetNativeHandles(), Spirv, Width, Height, PushConstantBytes);
    }
}
