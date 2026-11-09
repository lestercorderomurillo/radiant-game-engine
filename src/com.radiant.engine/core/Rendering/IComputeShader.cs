using System;

namespace com.radiant.engine.core;

/// <summary>Optional compute kernel with a session-owned output image.</summary>
public interface IComputeShader : IDisposable
{
    Texture2D Output { get; }
    void Dispatch(int GroupsX, int GroupsY, int GroupsZ = 1, ReadOnlySpan<byte> Constants = default);
    void Synchronize();
}

/// <summary>Optional compute commands. Backends without compute inherit an explicit unsupported result.</summary>
public interface IComputeRenderer
{
    bool SupportsComputeShaders(out string Reason)
    {
        Reason = "This renderer does not provide compute shaders.";
        return false;
    }
    IComputeShader CreateComputeShader(ReadOnlySpan<byte> Spirv, int Width, int Height, int PushConstantBytes = 0) =>
        throw new NotSupportedException("Compute is not supported by this renderer.");
}
