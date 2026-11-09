using System.Numerics;
using System.Runtime.InteropServices;

namespace com.radiant.engine.core;

/// <summary>Backend-independent mesh vertex: position, normal, UV; 32 tightly packed bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MeshVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TextureCoordinate;

    public MeshVertex(Vector3 Position, Vector3 Normal, Vector2 TextureCoordinate)
    {
        this.Position = Position;
        this.Normal = Normal;
        this.TextureCoordinate = TextureCoordinate;
    }
}
