using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using com.radiant.engine.core;
using com.radiant.engine.graphics;
using com.radiant.engine.runtime;

internal static class PortableRendererRegression
{
    public static void Run()
    {
        var backend = new Backend();
        using var loop = new GameLoop();
        using var renderer = new Renderer(backend, new TestWindow(), loop);
        Require(renderer.ScreenSize == new Vector2(640, 480), "Backend presentation metrics");
        renderer.ClearBackBuffer(Color.Black);
        var sceneTarget = renderer.SceneRT;
        using var texture = renderer.CreateTexture(1, 1);
        using var a = renderer.CreateRenderTarget(8, 8);
        using var b = renderer.CreateRenderTarget(8, 8);
        backend.Calls.Clear();
        renderer.DrawTexture(texture, Vector2.Zero).Clear(Color.Coral);
        Require(string.Join(',', backend.Calls) == "begin,sprite,end,clear", "Clear must follow pending sprites");

        var pair = new[] { a, b };
        renderer.SetTargets(pair).PushTargets();
        pair[0] = b;
        for (int i = 0; i < 40; i++) renderer.SetTargets(b, a);
        renderer.PopTargets();
        Require(ReferenceEquals(backend.Targets[0], a) && ReferenceEquals(backend.Targets[1], b), "Target snapshot aliases caller or reused ring storage");
        for (int i = 0; i < 40; i++) renderer.PushTargets().SetTarget(i % 2 == 0 ? a : b);
        for (int i = 39; i >= 0; i--)
        {
            renderer.PopTargets();
            Require(ReferenceEquals(backend.Targets[0], i == 0 || i % 2 != 0 ? a : b), "Nested target restoration");
        }

        backend.Calls.Clear();
        renderer.DrawTexture(texture, Vector2.One).DrawMesh(Mesh.Cube);
        renderer.DrawTriangles(new ColorVertex[3]);
        renderer.DrawShape(Shape.Rect(0, 0, 1, 1, Color.White)).FlushShapes(a);
        renderer.BeginDraw();
        renderer.DrawString("Inter", 12, "hello", Vector2.Zero, Color.White, bold: true);
        renderer.EndDraw();
        Require(string.Join(',', backend.Calls) == "begin,sprite,end,mesh,triangles,shapes,begin,text,text,end", "Portable command ordering");
        Require(renderer.ShapeBatchCount == 0, "Shape submission did not clear collection");
        renderer.SetTarget(a).PushTargets();
        renderer.FlushShapes(b);
        renderer.PushTargets().SetTarget(a).PopTargets();
        Require(ReferenceEquals(backend.Targets[0], b), "Shape submission target tracking");
        renderer.PopTargets();
        Require(ReferenceEquals(backend.Targets[0], a), "Shape submission must preserve prior target snapshot");
        Require(!renderer.SupportsComputeShaders(out _) && !renderer.SupportsVulkanPathTracing(out _), "Default capabilities");
        renderer.PresentToBackBuffer();
        Require(backend.Targets.Length == 0, "Presentation must bind backbuffer");
        renderer.PushTargets().SetTarget(a).PopTargets();
        Require(backend.Targets.Length == 0, "Presentation target snapshot");
        renderer.Dispose();
        renderer.Dispose();
        Require(backend.Disposals == 1 && sceneTarget.IsDisposed, "Renderer/backend lifetime");
        Console.WriteLine("PASS: actual Radiant renderer on an independent command backend: sprites, fonts, geometry, shapes, presentation, deep MRT restoration, command order and disposal.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class TestWindow : IWindow
    {
        public bool IsActive => true;
        public string Title { get; set; }
        public Rectangle ClientBounds => new(0, 0, 640, 480);
        public void RequestClose() { }
    }
    private sealed class Backend : IGraphicsBackend, IGraphicsCommands, IGeometryBackend, IShapeBatchBackend, IGraphicsResourceFactory
    {
        private readonly ResourceCacheRegression.MemoryBackend Memory = new();
        public readonly List<string> Calls = new();
        public RenderTarget2D[] Targets = [];
        public int Disposals;
        public IGraphicsResourceFactory Resources => this;
        public IGraphicsAssetLoader Assets => Memory;
        public IFontLoader Fonts { get; } = new FontServiceRegression.TestLoader();
        public IGraphicsCommands Commands => this;
        public IGeometryBackend Geometry => this;
        public IShapeBatchBackend Shapes => this;
        public Point PresentationSize => new(640, 480);
        public bool HasPendingResize => false;
        public void AcknowledgeResize() { }
        public Rectangle ViewportBounds => Targets.Length > 0 ? Targets[0].Bounds : new(0, 0, 640, 480);
        public void SetTargets(ReadOnlySpan<RenderTarget2D> targets) => Targets = targets.ToArray();
        public void Clear(Color color) => Calls.Add("clear");
        public void SetTexture(int slot, Texture2D texture) { }
        public void SetSampler(int slot, SamplerState sampler) { }
        public void DrawFullscreen(Effect shader, BlendState blend, DepthStencilState depth, RasterizerState rasterizer) => Calls.Add("fullscreen");
        public void BeginSprites(SpriteSortMode sort, BlendState blend, SamplerState sampler, DepthStencilState depth = null, RasterizerState rasterizer = null, Effect shader = null, Matrix? transform = null) => Calls.Add("begin");
        public void EndSprites() => Calls.Add("end");
        public void DrawSprite(Texture2D texture, Rectangle destination, Rectangle? source, Color color, float rotation = 0, Vector2 origin = default, SpriteEffects effects = SpriteEffects.None, float depth = 0) => Calls.Add("sprite");
        public void DrawSprite(Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth) => Calls.Add("sprite");
        public void DrawText(FontFace font, string text, Vector2 position, Color color, float scale) => Calls.Add("text");
        public void DrawTriangles(ColorVertex[] vertices, Effect shader, BlendState blend, DepthStencilState depth, RasterizerState rasterizer) => Calls.Add("triangles");
        public void DrawMesh(Mesh mesh, Effect shader, BlendState blend, DepthStencilState depth, RasterizerState rasterizer) => Calls.Add("mesh");
        public float LastSetDataMs => 0;
        public float LastDrawMs => 0;
        public void Submit(Shape[] shapes, int count, Matrix projection, RenderTarget2D target, Color? clear, BlendState blend, Effect shader, string technique) { Targets = [target]; Calls.Add("shapes"); }
        public void Dispose() => Disposals++;
        public Texture2D CreateTexture(int width, int height, SurfaceFormat format = SurfaceFormat.Color) => Memory.CreateTexture(width, height, format);
        public RenderTarget2D CreateRenderTarget(int width, int height, SurfaceFormat format = SurfaceFormat.Color, DepthFormat depth = DepthFormat.None, RenderTargetUsage usage = RenderTargetUsage.DiscardContents, int samples = 0) => new Target(width, height, format, depth, usage, samples);
        public int GetSupportedSamples(int requested, SurfaceFormat format = SurfaceFormat.Color, DepthFormat depth = DepthFormat.None) => 0;
    }
    private sealed class Target(int width, int height, SurfaceFormat format, DepthFormat depth, RenderTargetUsage usage, int samples) : RenderTarget2D
    {
        private bool Disposed;
        public override int Width => width;
        public override int Height => height;
        public override int LevelCount => 1;
        public override SurfaceFormat Format => format;
        public override DepthFormat DepthStencilFormat => depth;
        public override int MultiSampleCount => samples;
        public override RenderTargetUsage RenderTargetUsage => usage;
        public override bool IsDisposed => Disposed;
        public override void Dispose() => Disposed = true;
        public override void SetData<T>(int level, Rectangle? rectangle, T[] data, int start, int count) => throw new NotSupportedException();
        public override void GetData<T>(int level, Rectangle? rectangle, T[] data, int start, int count) => throw new NotSupportedException();
        public override void SaveAsPng(Stream stream, int width, int height) => throw new NotSupportedException();
    }
}
