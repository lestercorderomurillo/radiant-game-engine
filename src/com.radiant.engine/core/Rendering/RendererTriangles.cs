using System;
using Microsoft.Xna.Framework.Graphics;

#pragma warning disable CS0618
namespace com.radiant.engine.core;

public partial class Renderer
{
    private DynamicVertexBuffer TriangleVertices;
    public Renderer DrawTriangles(VertexPositionColor[] vertices)
    {
        if (vertices.Length == 0) return this;
        if (vertices.Length % 3 != 0) throw new ArgumentException("Triangle vertices must be a multiple of three.", nameof(vertices));
        CommitTextures();
        if (TriangleVertices == null || TriangleVertices.VertexCount < vertices.Length)
        {
            TriangleVertices?.Dispose();
            TriangleVertices = new DynamicVertexBuffer(Device, VertexPositionColor.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
        }
        TriangleVertices.SetData(vertices, 0, vertices.Length, SetDataOptions.Discard);
        Device.BlendState = BlendState;
        Device.DepthStencilState = DepthStencilState;
        Device.RasterizerState = RasterizerState;
        Device.SetVertexBuffer(TriangleVertices);
        foreach (var pass in CurrentShader.CurrentTechnique.Passes) { pass.Apply(); Device.DrawPrimitives(PrimitiveType.TriangleList, 0, vertices.Length / 3); }
        return this;
    }
}
