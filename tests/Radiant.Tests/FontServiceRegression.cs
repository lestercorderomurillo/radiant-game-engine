using System;
using System.Reflection;
using com.radiant.engine.core;
using com.radiant.engine.graphics;

internal static class FontServiceRegression
{
    public static void Run()
    {
        var type = typeof(Renderer).Assembly.GetType("com.radiant.engine.core.FontRegistry", true);
        var loader = new TestLoader();
        using var registry = (IDisposable)Activator.CreateInstance(type, loader);
        object Call(string name, params object[] args) => type.GetMethod(name).Invoke(registry, args);
        Call("Load", "test", "font.ttf");
        Call("Load", "test", "font.ttf");
        Require(loader.Loads == 1, "Font cache load count");
        type.GetProperty("FontRenderScale").SetValue(registry, 2f);
        Require((Vector2)Call("Measure", "test", 12f, "abc") == new Vector2(36, 12), "Supersampled metrics");
        Require((float)Call("GetLineHeight", "test", 12f) == 12f, "Scaled line height");
        Require(loader.Family.LastSize == 24f, "Rasterization size");
        var face = (FontFace)Call("GetFont", "test", 12f);
        Require(face.LineHeight == 12f, "Exact-size font face");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            try { type.GetProperty("FontRenderScale").SetValue(registry, invalid); throw new Exception("Invalid font scale accepted"); }
            catch (TargetInvocationException e) when (e.InnerException is ArgumentOutOfRangeException) { }
        }
        registry.Dispose();
        Require(loader.Family.Disposed, "Font family lifetime");
        // The public renderer no longer exports FontStashSharp font objects.
        Require(typeof(Renderer).GetMethod("GetFont").ReturnType == typeof(FontFace), "Native font return type");
        Console.WriteLine("PASS: backend-independent font cache, exact/supersampled metrics, invalid scales and family lifetime.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal sealed class TestLoader : IFontLoader
    {
        public int Loads;
        public readonly TestFamily Family = new();
        public IFontFamily Load(string path) { Loads++; return Family; }
    }
    internal sealed class TestFamily : IFontFamily
    {
        public float LastSize;
        public bool Disposed;
        public FontFace GetFont(float size) { LastSize = size; return new TestFace(size); }
        public void Dispose() => Disposed = true;
    }
    private sealed class TestFace(float size) : FontFace
    {
        public override float LineHeight => size;
        public override Vector2 Measure(string text) => new(size * text.Length, size);
    }
}
