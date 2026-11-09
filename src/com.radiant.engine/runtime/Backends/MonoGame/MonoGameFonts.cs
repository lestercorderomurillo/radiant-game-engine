using System;
using System.IO;
using System.Collections.Generic;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using com.radiant.engine.graphics;

namespace com.radiant.engine.runtime;

internal sealed class MonoGameFontLoader(string root) : IFontLoader
{
    public IFontFamily Load(string path)
    {
        #pragma warning disable CS0618
        var system = new FontSystem(new FontSystemSettings { PremultiplyAlpha = true });
        #pragma warning restore CS0618
        try
        {
            using var stream = TitleContainer.OpenStream(Path.Combine(root, path));
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            system.AddFont(bytes.ToArray());
            return new MonoGameFontFamily(system);
        }
        catch { system.Dispose(); throw; }
    }
}

internal sealed class MonoGameFontFamily(FontSystem system) : IFontFamily
{
    private readonly Dictionary<float, FontFace> Faces = new();
    private bool Disposed;
    internal void Check() => ObjectDisposedException.ThrowIf(Disposed, this);
    public FontFace GetFont(float size)
    {
        Check();
        if (!float.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (!Faces.TryGetValue(size, out var font)) Faces[size] = font = new MonoGameFontFace(this, system.GetFont(size));
        return font;
    }
    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        Faces.Clear();
        system.Dispose();
    }
}

internal sealed class MonoGameFontFace(MonoGameFontFamily owner, SpriteFontBase font) : FontFace
{
    public override float LineHeight { get { owner.Check(); return font.LineHeight; } }
    public override Vector2 Measure(string text) { owner.Check(); return font.MeasureString(text).ToNumerics(); }
    internal static void Draw(SpriteBatch batch, FontFace face, string text, Vector2 position, Color color, float scale)
    {
        if (face is not MonoGameFontFace native) throw new ArgumentException("Font belongs to another backend.", nameof(face));
        native.Draw(batch, text, position, color, scale);
    }
    private void Draw(SpriteBatch batch, string text, Vector2 position, Color color, float scale)
    {
        owner.Check();
        batch.DrawString(font, text, position, color.ToNative(), scale: new Vector2(scale));
    }
}
