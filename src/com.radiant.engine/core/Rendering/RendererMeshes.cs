using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#pragma warning disable CS0618
namespace com.radiant.engine.core;

public partial class Renderer
{
    private readonly Dictionary<Mesh, (VertexBuffer Vertices, IndexBuffer Indices)> MeshBuffers = new();

    /// <summary>Draw an indexed mesh with the configured shader and render state.</summary>
    public Renderer DrawMesh(Mesh mesh)
    {
        CommitTextures();
        if (!MeshBuffers.TryGetValue(mesh, out var buffers))
        {
            buffers.Vertices = new VertexBuffer(Device, VertexPositionNormalTexture.VertexDeclaration,
                mesh.VertexCount, BufferUsage.WriteOnly);
            buffers.Indices = new IndexBuffer(Device, IndexElementSize.ThirtyTwoBits,
                mesh.Indices.Length, BufferUsage.WriteOnly);
            buffers.Vertices.SetData(mesh.Vertices);
            buffers.Indices.SetData(mesh.Indices);
            MeshBuffers.Add(mesh, buffers);
        }
        Device.BlendState = BlendState;
        Device.DepthStencilState = DepthStencilState;
        Device.RasterizerState = RasterizerState;
        Device.SetVertexBuffer(buffers.Vertices);
        Device.Indices = buffers.Indices;
        foreach (var pass in CurrentShader.CurrentTechnique.Passes)
        {
            pass.Apply();
            Device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, mesh.TriangleCount);
        }
        return this;
    }

    private void DisposeMeshes()
    {
        foreach (var buffers in MeshBuffers.Values)
        {
            buffers.Vertices.Dispose();
            buffers.Indices.Dispose();
        }
        MeshBuffers.Clear();
    }
}
