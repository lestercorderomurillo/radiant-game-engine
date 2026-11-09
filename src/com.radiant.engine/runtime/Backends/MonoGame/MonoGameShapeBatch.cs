using System;
using System.Diagnostics;
using Microsoft.Xna.Framework.Graphics;
using com.radiant.engine.core;
using com.radiant.engine.graphics;

namespace com.radiant.engine.runtime;

internal sealed class MonoGameShapeBatch : IShapeBatchBackend
{
    private readonly GraphicsDevice Device;
    private readonly MonoGameRenderStates NativeStates = new();
    private readonly VertexBuffer QuadBuffer;
    private readonly IndexBuffer IndexBuffer;
    private DynamicVertexBuffer InstanceBuffer;
    private int Capacity;
    private bool Disposed;
    public float LastSetDataMs { get; private set; }
    public float LastDrawMs { get; private set; }
    private static readonly VertexDeclaration InstanceDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 2),
        new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 1),
        new VertexElement(20, VertexElementFormat.Single, VertexElementUsage.TextureCoordinate, 3),
        new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 4));

    internal MonoGameShapeBatch(GraphicsDevice device)
    {
        Device = device;
        var vertices = new ShapeQuadVertex[]
        {
            new(new Vector2(0, 0), new Vector2(0, 0)),
            new(new Vector2(1, 0), new Vector2(1, 0)),
            new(new Vector2(0, 1), new Vector2(0, 1)),
            new(new Vector2(1, 1), new Vector2(1, 1))
        };
        QuadBuffer = new VertexBuffer(Device, ShapeQuadVertex.Declaration, 4, BufferUsage.WriteOnly);
        QuadBuffer.SetData(vertices);
        IndexBuffer = new IndexBuffer(Device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
        IndexBuffer.SetData(new short[] { 0, 1, 2, 2, 1, 3 });
    }
    public void Submit(Shape[] shapes, int count, Matrix viewProjection, RenderTarget2D target,
        Color? clearColor, BlendState blendState, Effect shader, string technique)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ArgumentNullException.ThrowIfNull(shapes);
        if ((uint)count > (uint)shapes.Length) throw new ArgumentOutOfRangeException(nameof(count));
        MonoGameResources.NativeEffect(shader, Device);
        Device.SetRenderTarget(MonoGameResources.NativeTarget(target, Device));
        if (clearColor.HasValue) Device.Clear(clearColor.Value.ToNative());
        if (count == 0) return;
        if (count > Capacity)
        {
            int capacity = (int)Math.Min(Array.MaxLength, Math.Max((long)count, Math.Max(4L, (long)Capacity * 2)));
            var replacement = new DynamicVertexBuffer(Device, InstanceDeclaration, capacity, BufferUsage.WriteOnly);
            InstanceBuffer?.Dispose();
            InstanceBuffer = replacement;
            Capacity = capacity;
        }
        var upload = Stopwatch.GetTimestamp();
        InstanceBuffer.SetData(shapes, 0, count, SetDataOptions.Discard);
        LastSetDataMs = (float)Stopwatch.GetElapsedTime(upload).TotalMilliseconds;
        Device.BlendState = NativeStates.Get(blendState);
        Device.DepthStencilState = NativeStates.Get(DepthStencilState.None);
        Device.RasterizerState = NativeStates.Get(RasterizerState.CullNone);
        Device.SetVertexBuffers(new VertexBufferBinding(QuadBuffer, 0, 0), new VertexBufferBinding(InstanceBuffer, 0, 1));
        Device.Indices = IndexBuffer;
        shader.CurrentTechnique = shader.Techniques[technique];
        shader.Parameters["ViewProjection"]?.SetValue(viewProjection);
        var draw = Stopwatch.GetTimestamp();
        foreach (var pass in shader.CurrentTechnique.Passes)
        {
            pass.Apply();
            Device.DrawInstancedPrimitives(PrimitiveType.TriangleList, 0, 0, 2, count);
        }
        LastDrawMs = (float)Stopwatch.GetElapsedTime(draw).TotalMilliseconds;
    }
    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        NativeStates.Dispose();
        InstanceBuffer?.Dispose();
        QuadBuffer.Dispose();
        IndexBuffer.Dispose();
    }
}
