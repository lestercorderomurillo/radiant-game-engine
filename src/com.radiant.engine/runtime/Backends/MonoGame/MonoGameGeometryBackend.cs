using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using com.radiant.engine.core;
using com.radiant.engine.graphics;

namespace com.radiant.engine.runtime;

internal sealed class MonoGameGeometryBackend(GraphicsDevice device) : IGeometryBackend
{
    private readonly MonoGameRenderStates States = new();
    private readonly Dictionary<Mesh, (VertexBuffer Vertices, IndexBuffer Indices)> MeshBuffers = new();
    private DynamicVertexBuffer TriangleVertices;
    private bool Disposed;

    public void DrawTriangles(ColorVertex[] vertices, Effect shader, BlendState blend,
        DepthStencilState depth, RasterizerState rasterizer)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Length % 3 != 0)
            throw new ArgumentException("Triangle vertices must be a multiple of three.", nameof(vertices));
        if (vertices.Length == 0) return;
        MonoGameResources.NativeEffect(shader, device);

        if (TriangleVertices == null || TriangleVertices.VertexCount < vertices.Length)
        {
            var replacement = new DynamicVertexBuffer(device, VertexPositionColor.VertexDeclaration,
                vertices.Length, BufferUsage.WriteOnly);
            TriangleVertices?.Dispose();
            TriangleVertices = replacement;
        }

        TriangleVertices.SetData(vertices, 0, vertices.Length, SetDataOptions.Discard);
        ApplyStates(blend, depth, rasterizer);
        device.SetVertexBuffer(TriangleVertices);

        foreach (var pass in shader.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawPrimitives(PrimitiveType.TriangleList, 0, vertices.Length / 3);
        }
    }

    public void DrawMesh(Mesh mesh, Effect shader, BlendState blend,
        DepthStencilState depth, RasterizerState rasterizer)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ArgumentNullException.ThrowIfNull(mesh);
        MonoGameResources.NativeEffect(shader, device);

        if (!MeshBuffers.TryGetValue(mesh, out var buffers))
        {
            VertexBuffer vertices = null;
            IndexBuffer indices = null;
            try
            {
                vertices = new VertexBuffer(device, VertexPositionNormalTexture.VertexDeclaration,
                    mesh.VertexCount, BufferUsage.WriteOnly);
                indices = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits,
                    mesh.IndexData.Length, BufferUsage.WriteOnly);
                vertices.SetData(mesh.VertexData.ToArray());
                indices.SetData(mesh.IndexData.ToArray());
                buffers = (vertices, indices);
                MeshBuffers.Add(mesh, buffers);
            }
            catch
            {
                vertices?.Dispose();
                indices?.Dispose();
                throw;
            }
        }

        ApplyStates(blend, depth, rasterizer);
        device.SetVertexBuffer(buffers.Vertices);
        device.Indices = buffers.Indices;

        foreach (var pass in shader.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, mesh.TriangleCount);
        }
    }

    private void ApplyStates(BlendState blend, DepthStencilState depth, RasterizerState rasterizer)
    {
        device.BlendState = States.Get(blend);
        device.DepthStencilState = States.Get(depth);
        device.RasterizerState = States.Get(rasterizer);
    }

    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        TriangleVertices?.Dispose();

        foreach (var buffers in MeshBuffers.Values)
        {
            buffers.Vertices.Dispose();
            buffers.Indices.Dispose();
        }

        MeshBuffers.Clear();
        States.Dispose();
    }
}
