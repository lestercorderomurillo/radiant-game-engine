using System;
using System.Collections.Generic;
using com.radiant.engine.graphics;

namespace com.radiant.engine.core;

/// <summary>
/// Caches font families supplied by the active backend.
/// Fonts are rasterized dynamically at any requested size.
/// </summary>
internal class FontRegistry : IDisposable
{
    private readonly Dictionary<string, IFontFamily> Systems = new();
    private readonly IFontLoader Loader;

    /// <summary>
    /// Supersample multiplier for font rendering. Fonts rasterize at size * FontRenderScale
    /// and draw scaled down for sharp text at high DPI.
    /// </summary>
    private float Scale = 1f;
    public float FontRenderScale
    {
        get => Scale;
        set
        {
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            Scale = value;
        }
    }

    /// <summary>Uses the supplied backend loader for font assets.</summary>
    public FontRegistry(IFontLoader loader)
    {
        Loader = loader;
    }

    /// <summary>
    /// Loads a TTF font file and registers it under the given name.
    /// </summary>
    /// <param name="name">Font family name for later retrieval.</param>
    /// <param name="path">Path to TTF file relative to content root.</param>
    public void Load(string name, string path)
    {
        if (Systems.ContainsKey(name)) return;

        Systems[name] = Loader.Load(path);
    }

    /// <summary>
    /// Gets a Radiant FontFace at the exact requested pixel size (no supersampling).
    /// </summary>
    public FontFace GetFont(string name, float size)
    {
        if (!float.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        return Systems[name].GetFont(size);
    }

    /// <summary>
    /// Measures text dimensions accounting for FontRenderScale.
    /// </summary>
    public Vector2 Measure(string fontName, float size, string text)
    {
        var font = GetFont(fontName, size * FontRenderScale);
        return font.Measure(text) / FontRenderScale;
    }

    /// <summary>
    /// Gets the line height for a font at a specific size, accounting for FontRenderScale.
    /// </summary>
    public float GetLineHeight(string fontName, float size) =>
        GetFont(fontName, size * FontRenderScale).LineHeight / FontRenderScale;

    /// <summary>Disposes all font systems.</summary>
    public void Dispose()
    {
        foreach (var fontSystem in Systems.Values)
            fontSystem.Dispose();

        Systems.Clear();
    }
}
