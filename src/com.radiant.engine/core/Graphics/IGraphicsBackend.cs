using System;
using com.radiant.engine.core;

namespace com.radiant.engine.graphics;

/// <summary>
/// Per-renderer graphics session. The renderer owns this session; it owns and disposes
/// all command adapters. Resources returned by its factories are disposed by their callers.
/// No windowing framework or native device is required by this contract.
/// </summary>
public interface IGraphicsBackend : IDisposable
{
    IGraphicsResourceFactory Resources { get; }
    IGraphicsAssetLoader Assets { get; }
    IFontLoader Fonts { get; }
    IGraphicsCommands Commands { get; }
    IGeometryBackend Geometry { get; }
    IShapeBatchBackend Shapes { get; }
    Point PresentationSize { get; }
    bool HasPendingResize { get; }
    void AcknowledgeResize();
    bool SupportsComputeShaders(out string reason) { reason = "Compute is unavailable on this backend."; return false; }
    IComputeShader CreateComputeShader(IRenderer renderer, ReadOnlySpan<byte> code, int width, int height, int constants) =>
        throw new NotSupportedException("Compute is unavailable on this backend.");
    bool SupportsPathTracing(out string reason) { reason = "Hardware tracing is unavailable on this backend."; return false; }
    bool SupportsDenoising(out string reason) { reason = "Native denoising is unavailable on this backend."; return false; }
    IPathTraceSession CreatePathTracer(IRenderer renderer, int width, int height, bool signals) =>
        throw new NotSupportedException("Hardware tracing is unavailable on this backend.");
    ITraceDenoiserSession CreateDenoiser(IRenderer renderer, IPathTraceSession trace, int mode) =>
        throw new NotSupportedException("Native denoising is unavailable on this backend.");
}
