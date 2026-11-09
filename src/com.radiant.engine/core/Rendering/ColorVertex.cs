using System.Runtime.InteropServices;

namespace com.radiant.engine.core;

/// <summary>A position and RGBA8 color with a 16-byte GPU layout.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ColorVertex
{
    public Vector3 Position;
    public Color Color;
    public ColorVertex(Vector3 position, Color color) { Position = position; Color = color; }
}
