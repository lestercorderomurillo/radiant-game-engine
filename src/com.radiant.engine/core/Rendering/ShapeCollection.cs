using System;

namespace com.radiant.engine.core;

/// <summary>
/// CPU-only shape storage and per-worker collection. Each worker owns its slot;
/// initialize, resize and merge only after joining workers. Sorting is ascending Z.
/// </summary>
public sealed class ShapeCollection
{
    private Shape[] Shapes;
    public int Count { get; private set; }
    public ReadOnlySpan<Shape> Items => Shapes.AsSpan(0, Count);
    internal Shape[] Buffer => Shapes;
    public ShapeCollection(int capacity = 65536)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Shapes = new Shape[capacity];
    }
    internal static int Grow(int current, int required)
    {
        if (required < 0 || required > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(required));
        return Math.Max(required, current > Array.MaxLength / 2 ? Array.MaxLength : current * 2);
    }
    public void EnsureCapacity(int required)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(required);
        if (required > Shapes.Length) Array.Resize(ref Shapes, Grow(Shapes.Length, required));
    }
    public void Add(Shape shape)
    {
        EnsureCapacity(checked(Count + 1));
        Shapes[Count++] = shape;
    }
    public void Clear() => Count = 0;
    private int ParallelThreadCount;
    private Shape[][] ParallelShapeBuffers;
    private float[][] ParallelZBuffers;
    private int[] ParallelShapeCounts;
    private int[] ParallelMergeIndices;
    private int ParallelCapacityPerThread;
    private (float z, int thread)[] MergeHeap;
    private int MergeHeapSize;

    /// <summary>
    /// Allocates per-thread shape and Z buffers for parallel collection.
    /// </summary>
    public void InitializeParallel(int threadCount, int capacityPerThread = 16384)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threadCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityPerThread);
        ParallelThreadCount = threadCount;
        ParallelCapacityPerThread = capacityPerThread;
        ParallelShapeBuffers = new Shape[threadCount][];
        ParallelZBuffers = new float[threadCount][];
        ParallelShapeCounts = new int[threadCount];
        ParallelMergeIndices = new int[threadCount];

        MergeHeap = new (float, int)[threadCount];

        for (int i = 0; i < threadCount; i++)
        {
            ParallelShapeBuffers[i] = new Shape[capacityPerThread];
            ParallelZBuffers[i] = new float[capacityPerThread];
        }
    }

    /// <summary>Grows all per-thread buffers to the specified capacity.</summary>
    public void EnsureParallelCapacity(int capacityPerThread)
    {
        if (capacityPerThread <= ParallelCapacityPerThread) return;

        ParallelCapacityPerThread = capacityPerThread;

        for (int i = 0; i < ParallelThreadCount; i++)
        {
            if (ParallelShapeBuffers[i].Length < capacityPerThread) Array.Resize(ref ParallelShapeBuffers[i], capacityPerThread);
            if (ParallelZBuffers[i].Length < capacityPerThread) Array.Resize(ref ParallelZBuffers[i], capacityPerThread);
        }
    }

    /// <summary>Ensures parallel buffers can hold totalEntities divided across threads.</summary>
    public void EnsureParallelCapacityForEntities(int totalEntities)
    {
        EnsureCapacity(totalEntities);

        if (ParallelThreadCount == 0) throw new InvalidOperationException("Initialize parallel buffers first.");
        int perThread = (int)(((long)totalEntities + ParallelThreadCount - 1) / ParallelThreadCount);
        EnsureParallelCapacity(perThread);
    }

    /// <summary>Gets the shape buffer for a specific thread.</summary>
    public Shape[] GetParallelBuffer(int threadIndex) => ParallelShapeBuffers[threadIndex];

    /// <summary>Gets the Z buffer for a specific thread.</summary>
    public float[] GetParallelZBuffer(int threadIndex) => ParallelZBuffers[threadIndex];

    /// <summary>Sets the shape count for a thread after direct indexed writes.</summary>
    public void SetParallelCount(int threadIndex, int count)
    {
        if ((uint)count > (uint)ParallelShapeBuffers[threadIndex].Length) throw new ArgumentOutOfRangeException(nameof(count));
        ParallelShapeCounts[threadIndex] = count;
    }

    /// <summary>Appends a shape to a thread's buffer, auto-growing if needed.</summary>
    public void DrawParallel(int threadIndex, Shape shape)
    {
        var buffer = ParallelShapeBuffers[threadIndex];
        var count = ParallelShapeCounts[threadIndex];

        if (count >= buffer.Length)
        {
            int newCapacity = Grow(buffer.Length, checked(count + 1));
            Array.Resize(ref ParallelShapeBuffers[threadIndex], newCapacity);
            Array.Resize(ref ParallelZBuffers[threadIndex], newCapacity);
            buffer = ParallelShapeBuffers[threadIndex];
        }

        buffer[count] = shape;
        ParallelShapeCounts[threadIndex] = count + 1;
    }

    /// <summary>
    /// Collects all per-thread buffers into the main Shapes array via Array.Copy (unsorted).
    /// Resets per-thread counts.
    /// </summary>
    public void CollectParallel()
    {
        int totalCount = 0;

        for (int i = 0; i < ParallelThreadCount; i++)
            totalCount = checked(totalCount + ParallelShapeCounts[i]);

        EnsureCapacity(totalCount);

        int offset = 0;

        for (int threadIndex = 0; threadIndex < ParallelThreadCount; threadIndex++)
        {
            var buffer = ParallelShapeBuffers[threadIndex];
            var count = ParallelShapeCounts[threadIndex];

            Array.Copy(buffer, 0, Shapes, offset, count);
            offset += count;

            ParallelShapeCounts[threadIndex] = 0;
        }

        Count = totalCount;
    }

    /// <summary>Resets all per-thread shape counts without collecting.</summary>
    public void ClearParallel()
    {
        for (int i = 0; i < ParallelThreadCount; i++)
            ParallelShapeCounts[i] = 0;
    }

    /// <summary>
    /// Sorts a thread's shapes by Z value using an in-place index sort.
    /// Call from within parallel work after populating the buffer.
    /// </summary>
    public void SortBufferByZ(int threadIndex)
    {
        int count = ParallelShapeCounts[threadIndex];

        if (count <= 1) return;

        var shapes = ParallelShapeBuffers[threadIndex];
        var zValues = ParallelZBuffers[threadIndex];
        Array.Sort(zValues, shapes, 0, count);
    }

    /// <summary>
    /// K-way merge of all per-thread buffers into the main Shapes array, sorted by Z.
    /// Each thread's buffer must be pre-sorted via SortBufferByZ.
    /// Uses a min-heap for O(N log T) merge.
    /// </summary>
    public void CollectParallelSorted()
    {
        int totalCount = 0;

        for (int i = 0; i < ParallelThreadCount; i++)
            totalCount = checked(totalCount + ParallelShapeCounts[i]);

        if (totalCount == 0)
        {
            Count = 0;
            return;
        }

        EnsureCapacity(totalCount);

        Array.Clear(ParallelMergeIndices, 0, ParallelThreadCount);

        MergeHeapSize = 0;

        for (int threadIndex = 0; threadIndex < ParallelThreadCount; threadIndex++)
        {
            if (ParallelShapeCounts[threadIndex] > 0) MergeHeap[MergeHeapSize++] = (ParallelZBuffers[threadIndex][0], threadIndex);
        }

        HeapifyAll();

        int outputIndex = 0;

        while (MergeHeapSize > 0)
        {
            var (_, bestThread) = MergeHeap[0];

            int idx = ParallelMergeIndices[bestThread]++;
            Shapes[outputIndex++] = ParallelShapeBuffers[bestThread][idx];

            if (ParallelMergeIndices[bestThread] < ParallelShapeCounts[bestThread])
            {
                MergeHeap[0] = (ParallelZBuffers[bestThread][ParallelMergeIndices[bestThread]], bestThread);
                HeapSiftDown(0);
            }
            else
            {
                MergeHeap[0] = MergeHeap[--MergeHeapSize];
                if (MergeHeapSize > 0) HeapSiftDown(0);
            }
        }

        for (int i = 0; i < ParallelThreadCount; i++)
            ParallelShapeCounts[i] = 0;

        Count = totalCount;
    }

    /// <summary>Builds the priority heap used to merge ordered shape batches.</summary>
    private void HeapifyAll()
    {
        for (int i = MergeHeapSize / 2 - 1; i >= 0; i--)
            HeapSiftDown(i);
    }

    /// <summary>Restores heap ordering after advancing one shape batch.</summary>
    private void HeapSiftDown(int i)
    {
        while (true)
        {
            int smallest = i;
            int left = 2 * i + 1;
            int right = 2 * i + 2;

            if (left < MergeHeapSize && MergeHeap[left].z.CompareTo(MergeHeap[smallest].z) < 0) smallest = left;
            if (right < MergeHeapSize && MergeHeap[right].z.CompareTo(MergeHeap[smallest].z) < 0) smallest = right;

            if (smallest == i) break;

            (MergeHeap[i], MergeHeap[smallest]) = (MergeHeap[smallest], MergeHeap[i]);
            i = smallest;
        }
    }

}
