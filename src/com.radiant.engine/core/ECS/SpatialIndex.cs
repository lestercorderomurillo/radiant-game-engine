using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using com.radiant.engine.bundle;

namespace com.radiant.engine.core;

/// <summary>Indexes entity positions for exact, regional, and nearest-neighbor queries.</summary>
public class SpatialIndex
{
    private readonly ECS Ecs;
    private readonly float CellSize;
    private readonly float InverseCellSize;

    private const int InitialEntityCapacity = 1024;
    private const int InitialCellCapacity = 64;
    private const int MaxEntitiesPerCell = 256;

    // Cells still use dictionary (sparse cell keys)
    private readonly Dictionary<long, CellData> Cells;

    // Array-based entity data (entity ID as direct index)
    private EntitySpatialData[] EntityDataArray;
    private bool[] EntityExists;
    private int EntityCapacity;

    // Exact position lookup (position key -> entity)
    private readonly Dictionary<long, int> ExactLookup;

    private int[] ResultArray;
    private int ResultCount;

    private float[] DistanceCache;
    private int[] SortBuffer;

    /// <summary>Entity IDs occupying one spatial grid cell.</summary>
    private struct CellData
    {
        public int[] Entities;
        public int Count;

        /// <summary>Allocates the initial entity storage for a spatial cell.</summary>
        public CellData(int capacity)
        {
            Entities = new int[capacity];
            Count = 0;
        }
    }

    /// <summary>Cached cell and position data used when moving or removing an entity.</summary>
    private struct EntitySpatialData
    {
        public long CellKey;
        public int IndexInCell;
        public float PosX, PosY, PosZ;  // Inline instead of Vector3 to avoid struct copy
        public long ExactKey;
    }

    /// <summary>Creates the spatial grid with the requested cell dimensions.</summary>
    public SpatialIndex(ECS ecs, float cellSize = 64f)
    {
        Ecs = ecs;
        CellSize = cellSize;
        InverseCellSize = 1f / cellSize;

        Cells = new Dictionary<long, CellData>(InitialCellCapacity);

        EntityCapacity = InitialEntityCapacity;
        EntityDataArray = new EntitySpatialData[EntityCapacity];
        EntityExists = new bool[EntityCapacity];
        ExactLookup = new Dictionary<long, int>();

        ResultArray = new int[1024];
        DistanceCache = new float[1024];
        SortBuffer = new int[1024];
    }

    /// <summary>Expands entity-indexed storage to accommodate the requested identifier.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureEntityCapacity(int entityId)
    {
        if (entityId < EntityCapacity) return;

        int newCapacity = EntityCapacity;

        while (newCapacity <= entityId)
            newCapacity *= 2;

        Array.Resize(ref EntityDataArray, newCapacity);
        Array.Resize(ref EntityExists, newCapacity);
        EntityCapacity = newCapacity;
    }

    /// <summary>Combines integer cell coordinates into a spatial hash key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long PackCell(int x, int y, int z)
    {
        unchecked
        {
            return ((long)(x + 524288) << 40) | ((long)(y + 524288) << 20) | (long)(z + 524288);
        }
    }

    /// <summary>Maps a world coordinate to its grid-cell coordinate.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ToCell(float px, float py, float pz, out int cx, out int cy, out int cz)
    {
        cx = (int)MathF.Floor(px * InverseCellSize);
        cy = (int)MathF.Floor(py * InverseCellSize);
        cz = (int)MathF.Floor(pz * InverseCellSize);
    }

    /// <summary>Converts a world position into its spatial grid key.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long ToCellKey(float px, float py, float pz)
    {
        ToCell(px, py, pz, out int cx, out int cy, out int cz);
        return PackCell(cx, cy, cz);
    }

    /// <summary>Builds a lookup key that preserves the exact indexed coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long ToExactKey(float px, float py, float pz)
    {
        unchecked
        {
            int x = (int)(px * 100f) + 524288;
            int y = (int)(py * 100f) + 524288;
            int z = (int)(pz * 100f) + 524288;

            return ((long)x << 40) | ((long)y << 20) | (long)z;
        }
    }

    /// <summary>Expands the reusable spatial-query result buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureResultCapacity(int required)
    {
        if (ResultArray.Length < required)
        {
            int newSize = Math.Max(ResultArray.Length * 2, required);
            Array.Resize(ref ResultArray, newSize);
            Array.Resize(ref DistanceCache, newSize);
            Array.Resize(ref SortBuffer, newSize);
        }
    }

    /// <summary>Registers an entity's position in both cell and exact-position lookups.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(int entity, Vector3 position)
    {
        Insert(entity, position.X, position.Y, position.Z);
    }

    /// <summary>Registers an entity's position in both cell and exact-position lookups.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(int entity, float px, float py, float pz)
    {
        EnsureEntityCapacity(entity);

        long newCellKey = ToCellKey(px, py, pz);

        if (EntityExists[entity])
        {
            ref var existing = ref EntityDataArray[entity];
            long newExactKey = ToExactKey(px, py, pz);

            // Same cell - just update position in-place
            if (existing.CellKey == newCellKey)
            {
                // Update exact lookup if position changed
                if (existing.ExactKey != newExactKey)
                {
                    if (ExactLookup.TryGetValue(existing.ExactKey, out int e) && e == entity) ExactLookup.Remove(existing.ExactKey);
                    ExactLookup[newExactKey] = entity;
                }

                existing.PosX = px;
                existing.PosY = py;
                existing.PosZ = pz;
                existing.ExactKey = newExactKey;

                return;
            }

            // Different cell - remove from old cell and exact lookup
            if (ExactLookup.TryGetValue(existing.ExactKey, out int old) && old == entity)
                ExactLookup.Remove(existing.ExactKey);
            RemoveFromCell(entity, ref existing);
        }

        // Add to new cell
        if (!Cells.TryGetValue(newCellKey, out var cell))
        {
            cell = new CellData(MaxEntitiesPerCell);
            Cells[newCellKey] = cell;
        }

        if (cell.Count >= cell.Entities.Length) Array.Resize(ref cell.Entities, cell.Entities.Length * 2);

        int indexInCell = cell.Count++;
        cell.Entities[indexInCell] = entity;
        Cells[newCellKey] = cell;

        // Update entity data in-place
        long exactKey = ToExactKey(px, py, pz);
        ref var data = ref EntityDataArray[entity];
        data.CellKey = newCellKey;
        data.IndexInCell = indexInCell;
        data.PosX = px;
        data.PosY = py;
        data.PosZ = pz;
        data.ExactKey = exactKey;
        EntityExists[entity] = true;
        ExactLookup[exactKey] = entity;
    }

    /// <summary>Removes an entity from its current spatial grid cell.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromCell(int entity, ref EntitySpatialData data)
    {
        if (!Cells.TryGetValue(data.CellKey, out var cell)) return;

        int indexInCell = data.IndexInCell;
        int lastIndex = cell.Count - 1;

        if (indexInCell < lastIndex)
        {
            int lastEntity = cell.Entities[lastIndex];
            cell.Entities[indexInCell] = lastEntity;
            EntityDataArray[lastEntity].IndexInCell = indexInCell;
        }

        cell.Count--;
        Cells[data.CellKey] = cell;

        if (cell.Count == 0) Cells.Remove(data.CellKey);
    }

    /// <summary>Removes an entity from cell and exact-position lookups.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(int entity)
    {
        if (entity >= EntityCapacity || !EntityExists[entity]) return;

        ref var data = ref EntityDataArray[entity];
        RemoveFromCell(entity, ref data);
        if (ExactLookup.TryGetValue(data.ExactKey, out int e) && e == entity) ExactLookup.Remove(data.ExactKey);
        EntityExists[entity] = false;
    }

    /// <summary>Moves an indexed entity, updating cell membership when necessary.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(int entity, Vector3 position)
    {
        Insert(entity, position.X, position.Y, position.Z);
    }

    /// <summary>Removes all indexed entities and clears cached spatial results.</summary>
    public void Clear()
    {
        Cells.Clear();
        ExactLookup.Clear();
        Array.Clear(EntityExists, 0, EntityCapacity);
    }

    /// <summary>Looks up an entity at the exact indexed coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(Vector3 position)
    {
        return AtExact(position.X, position.Y, position.Z);
    }

    /// <summary>Looks up an entity at the exact indexed coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(float x, float y, float z)
    {
        long exactKey = ToExactKey(x, y, z);
        return ExactLookup.TryGetValue(exactKey, out int entity) ? entity : null;
    }

    /// <summary>Collects entities assigned to the grid cell containing the query position.</summary>
    public ReadOnlySpan<int> InCell(int cx, int cy, int cz)
    {
        long key = PackCell(cx, cy, cz);

        if (!Cells.TryGetValue(key, out var cell)) return ReadOnlySpan<int>.Empty;

        return new ReadOnlySpan<int>(cell.Entities, 0, cell.Count);
    }

    /// <summary>Collects entities assigned to the grid cell containing the query position.</summary>
    public ReadOnlySpan<int> InCell(Vector3 position)
    {
        ToCell(position.X, position.Y, position.Z, out int cx, out int cy, out int cz);
        return InCell(cx, cy, cz);
    }

    /// <summary>Collects entities within the requested distance of a world-space position.</summary>
    public ReadOnlySpan<int> InRadius(Vector3 center, float radius)
    {
        return InRadius(center.X, center.Y, center.Z, radius);
    }

    /// <summary>Collects entities within the requested distance of a world-space position.</summary>
    public ReadOnlySpan<int> InRadius(float cx, float cy, float cz, float radius)
    {
        ResultCount = 0;

        float radiusSq = radius * radius;

        int minX = (int)MathF.Floor((cx - radius) * InverseCellSize);
        int maxX = (int)MathF.Floor((cx + radius) * InverseCellSize);
        int minY = (int)MathF.Floor((cy - radius) * InverseCellSize);
        int maxY = (int)MathF.Floor((cy + radius) * InverseCellSize);
        int minZ = (int)MathF.Floor((cz - radius) * InverseCellSize);
        int maxZ = (int)MathF.Floor((cz + radius) * InverseCellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    long key = PackCell(x, y, z);

                    if (!Cells.TryGetValue(key, out var cell)) continue;

                    EnsureResultCapacity(ResultCount + cell.Count);

                    for (int i = 0; i < cell.Count; i++)
                    {
                        int entity = cell.Entities[i];
                        ref var data = ref EntityDataArray[entity];

                        float dx = data.PosX - cx;
                        float dy = data.PosY - cy;
                        float dz = data.PosZ - cz;
                        float distSq = dx * dx + dy * dy + dz * dz;

                        if (distSq <= radiusSq) ResultArray[ResultCount++] = entity;
                    }
                }
            }
        }

        return new ReadOnlySpan<int>(ResultArray, 0, ResultCount);
    }

    /// <summary>Collects entities inside the supplied world-space bounds.</summary>
    public ReadOnlySpan<int> InBox(Vector3 min, Vector3 max)
    {
        return InBox(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    }

    /// <summary>Collects entities inside the supplied world-space bounds.</summary>
    public ReadOnlySpan<int> InBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        ResultCount = 0;

        int minCX = (int)MathF.Floor(minX * InverseCellSize);
        int maxCX = (int)MathF.Floor(maxX * InverseCellSize);
        int minCY = (int)MathF.Floor(minY * InverseCellSize);
        int maxCY = (int)MathF.Floor(maxY * InverseCellSize);
        int minCZ = (int)MathF.Floor(minZ * InverseCellSize);
        int maxCZ = (int)MathF.Floor(maxZ * InverseCellSize);

        for (int x = minCX; x <= maxCX; x++)
        {
            for (int y = minCY; y <= maxCY; y++)
            {
                for (int z = minCZ; z <= maxCZ; z++)
                {
                    long key = PackCell(x, y, z);

                    if (!Cells.TryGetValue(key, out var cell)) continue;

                    EnsureResultCapacity(ResultCount + cell.Count);

                    for (int i = 0; i < cell.Count; i++)
                    {
                        int entity = cell.Entities[i];
                        ref var data = ref EntityDataArray[entity];

                        if (data.PosX >= minX && data.PosX <= maxX &&
                            data.PosY >= minY && data.PosY <= maxY &&
                            data.PosZ >= minZ && data.PosZ <= maxZ)
                        {
                            ResultArray[ResultCount++] = entity;
                        }
                    }
                }
            }
        }

        return new ReadOnlySpan<int>(ResultArray, 0, ResultCount);
    }

    /// <summary>Finds nearby entities ordered by distance from the query position.</summary>
    public ReadOnlySpan<int> Nearest(Vector3 center, int count, float maxRadius = float.MaxValue)
    {
        return Nearest(center.X, center.Y, center.Z, count, maxRadius);
    }

    /// <summary>Finds nearby entities ordered by distance from the query position.</summary>
    public ReadOnlySpan<int> Nearest(float cx, float cy, float cz, int count, float maxRadius = float.MaxValue)
    {
        var candidates = InRadius(cx, cy, cz, maxRadius);

        if (candidates.Length <= count) return candidates;

        EnsureResultCapacity(candidates.Length);

        for (int i = 0; i < candidates.Length; i++)
        {
            int entity = candidates[i];
            ref var data = ref EntityDataArray[entity];
            float dx = data.PosX - cx;
            float dy = data.PosY - cy;
            float dz = data.PosZ - cz;
            DistanceCache[i] = dx * dx + dy * dy + dz * dz;
            SortBuffer[i] = i;
        }

        PartialSort(candidates.Length, count);

        // Resolve entity IDs into SortBuffer first to avoid aliasing
        // (candidates is a span over ResultArray, writing ResultArray would corrupt reads)
        for (int i = 0; i < count; i++)
            SortBuffer[i] = candidates[SortBuffer[i]];

        ResultCount = count;
        Array.Copy(SortBuffer, 0, ResultArray, 0, count);

        return new ReadOnlySpan<int>(ResultArray, 0, ResultCount);
    }

    /// <summary>Orders the nearest requested results without sorting the entire candidate set.</summary>
    private void PartialSort(int length, int k)
    {
        int left = 0;
        int right = length - 1;

        while (left < right)
        {
            int pivotIndex = Partition(left, right);

            if (pivotIndex == k - 1) return;

            if (pivotIndex < k - 1)
                left = pivotIndex + 1;
            else
                right = pivotIndex - 1;
        }
    }

    /// <summary>Partitions spatial candidates around a distance pivot for nearest-neighbor selection.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Partition(int left, int right)
    {
        float pivot = DistanceCache[SortBuffer[right]];
        int i = left - 1;

        for (int j = left; j < right; j++)
        {
            if (DistanceCache[SortBuffer[j]] <= pivot)
            {
                i++;
                (SortBuffer[i], SortBuffer[j]) = (SortBuffer[j], SortBuffer[i]);
            }
        }

        (SortBuffer[i + 1], SortBuffer[right]) = (SortBuffer[right], SortBuffer[i + 1]);

        return i + 1;
    }
}
