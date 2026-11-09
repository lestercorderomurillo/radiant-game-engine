using System;
using com.radiant.engine.graphics;

namespace com.radiant.engine.core;

/// <summary>Coordinates portable shape collection and a backend-owned upload/draw path.</summary>
internal sealed class ShapeBatcher(IShapeBatchBackend backend) : IDisposable
{
    private readonly ShapeCollection Collection = new();
    public Matrix ViewProjection { get; set; }
    public int Count => Collection.Count;
    public float LastSetDataMs => backend.LastSetDataMs;
    public float LastDrawMs => backend.LastDrawMs;
    public void Add(Shape shape) => Collection.Add(shape);
    public void Clear() => Collection.Clear();
    public void InitializeParallel(int threadCount, int capacityPerThread = 16384) => Collection.InitializeParallel(threadCount, capacityPerThread);
    public void EnsureParallelCapacity(int capacityPerThread) => Collection.EnsureParallelCapacity(capacityPerThread);
    public void EnsureParallelCapacityForEntities(int totalEntities) => Collection.EnsureParallelCapacityForEntities(totalEntities);
    public Shape[] GetParallelBuffer(int threadIndex) => Collection.GetParallelBuffer(threadIndex);
    public float[] GetParallelZBuffer(int threadIndex) => Collection.GetParallelZBuffer(threadIndex);
    public void SetParallelCount(int threadIndex, int count) => Collection.SetParallelCount(threadIndex, count);
    public void DrawParallel(int threadIndex, Shape shape) => Collection.DrawParallel(threadIndex, shape);
    public void CollectParallel() => Collection.CollectParallel();
    public void ClearParallel() => Collection.ClearParallel();
    public void SortBufferByZ(int threadIndex) => Collection.SortBufferByZ(threadIndex);
    public void CollectParallelSorted() => Collection.CollectParallelSorted();

    public void Flush(RenderTarget2D target, Color? clearColor, BlendState blendState, Effect shader, string technique)
    {
        backend.Submit(Collection.Buffer, Collection.Count, ViewProjection, target, clearColor, blendState, shader, technique);
        Collection.Clear();
    }
    public void FlushExternal(Shape[] shapes, int count, RenderTarget2D target, Color? clearColor, BlendState blendState, Effect shader, string technique) =>
        backend.Submit(shapes, count, ViewProjection, target, clearColor, blendState, shader, technique);
    public void Dispose() => backend.Dispose();
}
