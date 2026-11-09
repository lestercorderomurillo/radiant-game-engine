using System;
using System.Runtime.InteropServices;

namespace com.radiant.engine.math;

/// <summary>RGBA8 color. Normalized constructors clamp to [0,1]; no implicit color-space conversion.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Color : IEquatable<Color>
{
    public byte R, G, B, A;
    public Color(int r, int g, int b, int a = 255)
    {
        R = (byte)Math.Clamp(r, 0, 255); G = (byte)Math.Clamp(g, 0, 255);
        B = (byte)Math.Clamp(b, 0, 255); A = (byte)Math.Clamp(a, 0, 255);
    }
    public Color(float r, float g, float b, float a = 1) : this(Quantize(r), Quantize(g), Quantize(b), Quantize(a)) { }
    public Color(Vector3 rgb) : this(rgb.X, rgb.Y, rgb.Z) { }
    public Color(Vector4 rgba) : this(rgba.X, rgba.Y, rgba.Z, rgba.W) { }
    public Color(uint packedValue) { R = G = B = A = 0; PackedValue = packedValue; }
    private static int Quantize(float value) => (int)(Math.Clamp(value, 0, 1) * 255);
    /// <summary>R in the least significant byte, followed by G, B, A.</summary>
    public uint PackedValue
    {
        readonly get => (uint)(R | G << 8 | B << 16 | A << 24);
        set { R = (byte)value; G = (byte)(value >> 8); B = (byte)(value >> 16); A = (byte)(value >> 24); }
    }
    public readonly Vector3 ToVector3() => new Vector3(R / 255f, G / 255f, B / 255f);
    public readonly Vector4 ToVector4() => new Vector4(R / 255f, G / 255f, B / 255f, A / 255f);
    public readonly bool Equals(Color other) => PackedValue == other.PackedValue;
    public override readonly bool Equals(object obj) => obj is Color other && Equals(other);
    public override readonly int GetHashCode() => (int)PackedValue;
    public static bool operator ==(Color a, Color b) => a.Equals(b);
    public static bool operator !=(Color a, Color b) => !a.Equals(b);
    public static Color operator *(Color color, float scale) => new Color((int)(color.R * scale), (int)(color.G * scale), (int)(color.B * scale), (int)(color.A * scale));
    public override readonly string ToString() => $"{{R:{R} G:{G} B:{B} A:{A}}}";
    public static Color Transparent => new Color(0, 0, 0, 0);
    public static Color Red => new Color(255, 0, 0);
    public static Color Black => new Color(0, 0, 0);
    public static Color White => new Color(255, 255, 255);
    public static Color Coral => new Color(255, 127, 80);
    public static Color CornflowerBlue => new Color(100, 149, 237);
    public static Color Gold => new Color(255, 215, 0);
    public static Color Gray => new Color(128, 128, 128);
    public static Color Lime => new Color(0, 255, 0);
    public static Color Orange => new Color(255, 165, 0);
    public static Color OrangeRed => new Color(255, 69, 0);
}
