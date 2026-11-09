using System;

namespace com.radiant.engine.graphics;

/// <summary>A sized font borrowed from its family. It becomes invalid when that family is disposed.</summary>
public abstract class FontFace
{
    public abstract float LineHeight { get; }
    public abstract Vector2 Measure(string text);
}

public interface IFontFamily : IDisposable
{
    FontFace GetFont(float size);
}

/// <summary>Loads a caller-owned family from a backend-relative asset path.</summary>
public interface IFontLoader
{
    IFontFamily Load(string path);
}
