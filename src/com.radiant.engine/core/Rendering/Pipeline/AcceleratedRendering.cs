using System;

namespace com.radiant.engine.core;

/// <summary>Optional accelerated path tracing with a session-owned Radiant output.</summary>
public interface IPathTraceSession : IDisposable
{
    bool DenoisingSignals { get; }
    RenderTarget2D Sample { get; }
    int BuildCount { get; }
    float GpuMilliseconds { get; }
    void Upload(ITraceGeometry geometry);
    void Trace(ITraceGeometry geometry, Vector3 environment, int sequence, int bounces, bool stablePrimary, int denoiser);
}

/// <summary>Optional reconstruction of signals produced by a compatible tracing session.</summary>
public interface ITraceDenoiserSession : IDisposable
{
    RenderTarget2D Output { get; }
    void Render(IPathTraceSession trace, TraceCamera camera, bool reset);
}

public interface IAcceleratedRenderer
{
    bool SupportsPathTracing(out string reason) { reason = "Accelerated tracing is unavailable."; return false; }
    bool SupportsDenoising(out string reason) { reason = "Signal denoising is unavailable."; return false; }
    IPathTraceSession CreatePathTracer(int width, int height, bool signals) =>
        throw new NotSupportedException("This renderer does not provide accelerated tracing.");
    ITraceDenoiserSession CreateDenoiser(IPathTraceSession trace, int mode) =>
        throw new NotSupportedException("This renderer does not provide native denoising.");
}
