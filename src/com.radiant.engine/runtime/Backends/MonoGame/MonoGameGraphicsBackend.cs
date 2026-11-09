using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using Microsoft.Xna.Framework.Graphics;
using com.radiant.engine.core;
using com.radiant.engine.graphics;

namespace com.radiant.engine.runtime;

internal sealed class MonoGameGraphicsBackend : IGraphicsBackend
{
    private readonly Window Host;
    private GraphicsDevice Device => Host.GraphicsDevice;
    private readonly SpriteBatch Sprites;
    private bool Disposed;
    public IGraphicsResourceFactory Resources { get; }
    public IGraphicsAssetLoader Assets { get; }
    public IFontLoader Fonts { get; }
    public IGraphicsCommands Commands { get; }
    public IGeometryBackend Geometry { get; }
    public IShapeBatchBackend Shapes { get; }
    public Point PresentationSize => new(Device.PresentationParameters.BackBufferWidth, Device.PresentationParameters.BackBufferHeight);
    public bool HasPendingResize => Host.ResizePending;
    public void AcknowledgeResize() => Host.ClearResizePending();

    internal MonoGameGraphicsBackend(Window host)
    {
        Host = host;
        try
        {
            Resources = new MonoGameResourceFactory(Device);
            Assets = new MonoGameAssetLoader(host.Content);
            Fonts = new MonoGameFontLoader(host.Content.RootDirectory);
            Sprites = new SpriteBatch(Device);
            Commands = new MonoGameGraphicsCommands(Device, Sprites);
            Geometry = new MonoGameGeometryBackend(Device);
            Shapes = new MonoGameShapeBatch(Device);
        }
        catch { Dispose(); throw; }
    }

    public bool SupportsComputeShaders(out string reason) => ComputeShader.IsSupported(Device.GetNativeHandles(), out reason);
    public bool SupportsPathTracing(out string reason) => VulkanPathTraceSession.IsSupported(Device.GetNativeHandles(), out reason);
    public bool SupportsDenoising(out string reason) => VulkanNrdSession.IsSupported(Device.GetNativeHandles(), out reason);
    public IComputeShader CreateComputeShader(IRenderer renderer, ReadOnlySpan<byte> code, int width, int height, int constants)
    {
        if (!SupportsComputeShaders(out var reason)) throw new NotSupportedException(reason);
        return new ComputeShader(renderer, this, Device.GetNativeHandles(), code, width, height, constants);
    }
    public IPathTraceSession CreatePathTracer(IRenderer renderer, int width, int height, bool signals)
    {
        if (!SupportsPathTracing(out var reason)) throw new NotSupportedException(reason);
        return new VulkanPathTraceSession(renderer, this, Device.GetNativeHandles(), width, height, signals);
    }
    public ITraceDenoiserSession CreateDenoiser(IRenderer renderer, IPathTraceSession trace, int mode)
    {
        if (!SupportsDenoising(out var reason)) throw new NotSupportedException(reason);
        if (trace is not VulkanPathTraceSession native || !ReferenceEquals(native.Backend, this))
            throw new ArgumentException("Tracing session belongs to a different graphics backend.", nameof(trace));
        return new VulkanNrdSession(renderer, this, native, mode);
    }
    internal RenderTarget2D WrapImage(IntPtr image, int width, int height) =>
        new MonoGameRenderTarget(Microsoft.Xna.Framework.Graphics.RenderTarget2D.FromNativeHandle(Device, image, width, height,
            Microsoft.Xna.Framework.Graphics.SurfaceFormat.Vector4, Microsoft.Xna.Framework.Graphics.DepthFormat.None, 0, false));

    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        Commands?.Dispose();
        Geometry?.Dispose();
        Shapes?.Dispose();
        Sprites?.Dispose();
    }
}
