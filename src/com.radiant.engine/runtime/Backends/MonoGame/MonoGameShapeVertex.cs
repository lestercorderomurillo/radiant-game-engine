using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.runtime;

/// <summary>
/// Vertex type for the instanced shape quad template (position + UV).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ShapeQuadVertex : IVertexType
{
    public Vector2 Position;
    public Vector2 UV;

    public static readonly VertexDeclaration Declaration = new VertexDeclaration(
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
    );

    VertexDeclaration IVertexType.VertexDeclaration => Declaration;

    /// <summary>Stores one quad corner's position and texture coordinates.</summary>
    public ShapeQuadVertex(Vector2 position, Vector2 uv)
    {
        Position = position;
        UV = uv;
    }
}
