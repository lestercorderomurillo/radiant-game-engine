using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using com.radiant.engine.core;
using com.radiant.engine.graphics;

internal static class ResourceCacheRegression
{
    public static void Run()
    {
        var backend = new MemoryBackend();
        var assembly = typeof(Renderer).Assembly;
        using var textures = (IDisposable)Activator.CreateInstance(assembly.GetType("com.radiant.engine.core.TextureManager", true), backend, backend);
        using var shaders = (IDisposable)Activator.CreateInstance(assembly.GetType("com.radiant.engine.core.ShaderRegistry", true), backend);
        object Call(object service, string name, params object[] args) => service.GetType().GetMethod(name).Invoke(service, args);
        var first = (Texture2D)Call(textures, "Get", "sprites/test");
        Require(ReferenceEquals(first, Call(textures, "Get", "sprites/test")) && backend.TextureLoads == 1, "Content cache identity");
        var solid = (MemoryTexture)Call(textures, "GetSolid", Color.Coral, 3, 2);
        Require(solid.Width == 3 && solid.Height == 2 && solid.Pixels.Length == 6 && solid.Pixels.All(pixel => pixel == Color.Coral), "Portable solid generator");
        Require(ReferenceEquals(solid, Call(textures, "GetSolid", Color.Coral, 3, 2)), "Solid cache identity");
        var circle = (MemoryTexture)Call(textures, "GetShape", "Circle", 16);
        Require(circle.Pixels[0].A == 0 && circle.Pixels[8 * 16 + 8].A == 255, "Portable circle coverage");
        var shader = (Effect)Call(shaders, "Load", "test");
        Require(ReferenceEquals(shader, Call(shaders, "Get", "test")) && backend.EffectLoads == 1 && backend.LastEffect == "shaders/test", "Shader cache/load boundary");
        Require((bool)Call(shaders, "Release", "test") && shader.IsDisposed, "Shader release");
        var reloaded = (Effect)Call(shaders, "Load", "test");
        Require(!ReferenceEquals(shader, reloaded) && !reloaded.IsDisposed && backend.EffectLoads == 2, "Shader reload");
        textures.Dispose();
        shaders.Dispose();
        Require(first.IsDisposed && solid.IsDisposed && circle.IsDisposed && reloaded.IsDisposed, "Cache resource cleanup");
        Console.WriteLine("PASS: shared asset/solid/shape/shader caches with an independent in-memory resource backend, reuse, reload and cleanup.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal sealed class MemoryBackend : IGraphicsResourceFactory, IGraphicsAssetLoader
    {
        public int TextureLoads, EffectLoads;
        public string LastEffect;
        public Texture2D LoadTexture(string name) { TextureLoads++; return CreateTexture(1, 1); }
        public Effect LoadEffect(string name) { EffectLoads++; LastEffect = name; return new MemoryEffect(); }
        public Texture2D CreateTexture(int width, int height, SurfaceFormat format = SurfaceFormat.Color) => new MemoryTexture(width, height);
        public RenderTarget2D CreateRenderTarget(int width, int height, SurfaceFormat format = SurfaceFormat.Color, DepthFormat depth = DepthFormat.None, RenderTargetUsage usage = RenderTargetUsage.DiscardContents, int samples = 0) => throw new NotSupportedException();
        public int GetSupportedSamples(int requested, SurfaceFormat format = SurfaceFormat.Color, DepthFormat depth = DepthFormat.None) => 0;
    }
    internal sealed class MemoryTexture(int width, int height) : Texture2D
    {
        public Color[] Pixels;
        private bool Disposed;
        public override int Width => width;
        public override int Height => height;
        public override int LevelCount => 1;
        public override SurfaceFormat Format => SurfaceFormat.Color;
        public override bool IsDisposed => Disposed;
        public override void Dispose() => Disposed = true;
        public override void SetData<T>(int level, Rectangle? rectangle, T[] data, int startIndex, int elementCount)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Require(level == 0 && rectangle == null, "Unexpected upload layout");
            Pixels = ((Color[])(object)data).Skip(startIndex).Take(elementCount).ToArray();
        }
        public override void GetData<T>(int level, Rectangle? rectangle, T[] data, int startIndex, int elementCount) => throw new NotSupportedException();
        public override void SaveAsPng(Stream stream, int width, int height) => throw new NotSupportedException();
    }
    internal sealed class MemoryEffect : Effect
    {
        private bool Disposed;
        public override bool IsDisposed => Disposed;
        public override void Dispose() => Disposed = true;
        public override EffectParameters Parameters => throw new NotSupportedException();
        public override IReadOnlyDictionary<string, EffectTechnique> Techniques => throw new NotSupportedException();
        public override EffectTechnique CurrentTechnique { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }
}
