using System;

namespace com.radiant.engine.core;

public partial class Renderer
{
    public bool SupportsComputeShaders(out string reason) => Backend.SupportsComputeShaders(out reason);
    public IComputeShader CreateComputeShader(ReadOnlySpan<byte> code, int width, int height, int pushConstantBytes = 0)
    {
        CommitTextures();
        return Backend.CreateComputeShader(this, code, width, height, pushConstantBytes);
    }
}
