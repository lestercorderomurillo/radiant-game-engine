using System;
using System.Collections.Generic;
using com.radiant.engine.graphics;

namespace com.radiant.engine.core;

/// <summary>
/// Manages cached textures: content assets, solid colors, and procedural shape textures.
/// Shape textures use a registry pattern — each shape type is registered with a generator
/// function and cached by size. Icons are plain content textures in presets/icons/.
/// </summary>
internal class TextureManager : IDisposable
{
    /// <summary>
    /// A registered shape type with its generator function and per-size cache.
    /// </summary>
    private sealed class ShapeEntry
    {
        public readonly Func<IGraphicsResourceFactory, int, Texture2D> Generator;
        public readonly int MinSize;
        public readonly Dictionary<int, Texture2D> Cache = new();

        /// <summary>Stores a procedural texture generator and its minimum cache size.</summary>
        public ShapeEntry(Func<IGraphicsResourceFactory, int, Texture2D> generator, int minSize)
        {
            Generator = generator;
            MinSize = minSize;
        }
    }

    private readonly IGraphicsResourceFactory Resources;
    private readonly IGraphicsAssetLoader Assets;
    private readonly Dictionary<string, Texture2D> ContentCache = new();
    private readonly Dictionary<(Color, int, int), Texture2D> SolidCache = new();
    private readonly Dictionary<string, ShapeEntry> ShapeEntries = new();

    /// <summary>Binds procedural texture caching to the supplied resource factory.</summary>
    public TextureManager(IGraphicsResourceFactory resources, IGraphicsAssetLoader assets)
    {
        Resources = resources;
        Assets = assets;

        RegisterShape("Circle", GenerateCircle, minSize: 1);
        RegisterShape("Triangle", GenerateTriangle, minSize: 4);
        RegisterShape("RoundedRect", GenerateRoundedRect, minSize: 1);
    }

    /// <summary>
    /// Registers a procedural shape texture generator with a minimum size clamp.
    /// </summary>
    public void RegisterShape(string name, Func<IGraphicsResourceFactory, int, Texture2D> generator, int minSize = 1)
    {
        ShapeEntries[name] = new ShapeEntry(generator, minSize);
    }

    /// <summary>
    /// Gets or creates a cached procedural shape texture by name and size.
    /// </summary>
    public Texture2D GetShape(string name, int size)
    {
        var entry = ShapeEntries[name];
        if (size < entry.MinSize) size = entry.MinSize;

        if (!entry.Cache.TryGetValue(size, out var texture))
        {
            texture = entry.Generator(Resources, size);
            entry.Cache[size] = texture;
        }

        return texture;
    }

    /// <summary>
    /// Loads and caches a content texture by asset name.
    /// </summary>
    public Texture2D Get(string name)
    {
        if (!ContentCache.TryGetValue(name, out var texture))
        {
            texture = Assets.LoadTexture(name);
            ContentCache[name] = texture;
        }

        return texture;
    }

    /// <summary>
    /// Gets or creates a cached solid color texture.
    /// </summary>
    public Texture2D GetSolid(Color color, int width = 1, int height = 1)
    {
        var key = (color, width, height);

        if (!SolidCache.TryGetValue(key, out var texture))
        {
            texture = Resources.CreateTexture(width, height);

            var data = new Color[width * height];
            Array.Fill(data, color);
            texture.SetData(data);
            SolidCache[key] = texture;
        }

        return texture;
    }

    /// <summary>Rasterizes a reusable circle texture for shape drawing.</summary>
    private static Texture2D GenerateCircle(IGraphicsResourceFactory resources, int diameter)
    {
        var texture = resources.CreateTexture(diameter, diameter);
        var data = new Color[diameter * diameter];

        float radius = diameter / 2f;
        float centerX = radius - 0.5f;
        float centerY = radius - 0.5f;

        const float aaWidth = 1.0f;
        float innerRadius = radius - aaWidth * 0.5f;

        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                float dx = x - centerX;
                float dy = y - centerY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                float alpha = 1.0f - global::System.Math.Clamp((dist - innerRadius) / aaWidth, 0f, 1f);
                byte a = (byte)(alpha * 255f + 0.5f);

                data[y * diameter + x] = new Color(a, a, a, a);
            }
        }

        texture.SetData(data);

        return texture;
    }

    /// <summary>Rasterizes a reusable triangle texture for shape drawing.</summary>
    private static Texture2D GenerateTriangle(IGraphicsResourceFactory resources, int size)
    {
        var texture = resources.CreateTexture(size, size);
        var data = new Color[size * size];
        float half = size / 2f;
        Vector2 a = new(half, size - 0.5f);
        Vector2 b = new(0.5f, 0.5f);
        Vector2 c = new(size - 0.5f, 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new(x + 0.5f, y + 0.5f);
                float d0 = EdgeDist(p, b, c);
                float d1 = EdgeDist(p, c, a);
                float d2 = EdgeDist(p, a, b);
                float dist = MathF.Max(d0, MathF.Max(d1, d2));
                float alpha = global::System.Math.Clamp(0.5f - dist, 0f, 1f);
                byte val = (byte)(alpha * 255f + 0.5f);
                data[y * size + x] = new Color(val, val, val, val);
            }
        }

        texture.SetData(data);

        return texture;

        static float EdgeDist(Vector2 p, Vector2 v0, Vector2 v1)
        {
            Vector2 edge = v1 - v0;
            Vector2 normal = new(edge.Y, -edge.X);
            float len = normal.Length();

            return (normal.X * (p.X - v0.X) + normal.Y * (p.Y - v0.Y)) / len;
        }
    }

    /// <summary>Rasterizes a reusable rounded-rectangle texture for shape drawing.</summary>
    private static Texture2D GenerateRoundedRect(IGraphicsResourceFactory resources, int radius)
    {
        int size = radius * 2 + 2;
        var texture = resources.CreateTexture(size, size);
        var data = new Color[size * size];
        float center = size / 2f;
        float innerDist = center - radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float dx = MathF.Max(MathF.Abs(px - center) - innerDist, 0f);
                float dy = MathF.Max(MathF.Abs(py - center) - innerDist, 0f);
                float dist = MathF.Sqrt(dx * dx + dy * dy) - radius;
                float alpha = global::System.Math.Clamp(0.5f - dist, 0f, 1f);
                byte a = (byte)(alpha * 255f + 0.5f);
                data[y * size + x] = new Color(a, a, a, a);
            }
        }

        texture.SetData(data);

        return texture;
    }

    /// <summary>Releases cached procedural textures.</summary>
    public void Dispose()
    {
        foreach (var texture in ContentCache.Values)
            texture?.Dispose();

        ContentCache.Clear();

        foreach (var texture in SolidCache.Values)
            texture?.Dispose();

        SolidCache.Clear();

        foreach (var entry in ShapeEntries.Values)
            foreach (var texture in entry.Cache.Values)
                texture?.Dispose();

        ShapeEntries.Clear();
    }
}
