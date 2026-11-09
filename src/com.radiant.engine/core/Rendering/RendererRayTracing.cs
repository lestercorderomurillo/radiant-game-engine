namespace com.radiant.engine.core;

public partial class Renderer
{
    public bool SupportsPathTracing(out string reason) => Backend.SupportsPathTracing(out reason);
    public bool SupportsVulkanPathTracing(out string reason) => SupportsPathTracing(out reason);
    public bool SupportsDenoising(out string reason) => Backend.SupportsDenoising(out reason);
    public bool SupportsNrdDenoising(out string reason) => SupportsDenoising(out reason);
    public IPathTraceSession CreatePathTracer(int width, int height, bool signals)
    {
        CommitTextures();
        return Backend.CreatePathTracer(this, width, height, signals);
    }
    public ITraceDenoiserSession CreateDenoiser(IPathTraceSession trace, int mode)
    {
        CommitTextures();
        return Backend.CreateDenoiser(this, trace, mode);
    }
}
