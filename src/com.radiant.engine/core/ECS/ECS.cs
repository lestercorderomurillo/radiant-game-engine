using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using com.radiant.engine.bundle;

namespace com.radiant.engine.core;

/// <summary>Owns entity storage, spatial queries, and dependency-ordered systems for one scene.</summary>
public class ECS : IGameObject
{
    // Entity tracking
    private readonly Stack<int> RecycledIds;
    private int NextEntityId;
    private int EntityCountValue;

    // Archetype storage - components live here
    private readonly List<Archetype> Archetypes;
    private readonly Dictionary<ulong, Archetype> ArchetypesBySignature;
    private readonly Dictionary<Type, int> TypeToId;
    private int NextTypeId;

    // Entity → location mapping
    private EntityRecord[] EntityRecords;
    private const int InitialEntityCapacity = 1024;

    /// <summary>Maps an entity ID to its archetype and component-array slot.</summary>
    private struct EntityRecord
    {
        public Archetype Arch;
        public int Index;
    }

    // Tags — lightweight string-based entity grouping
    private readonly Dictionary<string, PagedBitSet> TagSets = new();

    // Disabled entities — skipped by Query, still alive and restorable
    private readonly PagedBitSet DisabledEntities = new();

    // Deferred destruction — queued during frame, flushed at start of next Update
    private readonly HashSet<int> DeferredDestroyQueue = new();

    // Systems
    private readonly List<System> Systems;
    private List<System> RenderSystems;
    private readonly Dictionary<Type, System> SystemCache;
    private readonly List<SystemGroup> SystemGroups = new();
    private readonly Dictionary<System, SystemGroup> SystemToGroup = new();

    // Thread pool
    private static readonly int CachedThreadCount = Environment.ProcessorCount;

    public int EntityCount => EntityCountValue;
    /// <summary>Entity membership, component layout and enabled-state changes; used by tooling caches.</summary>
    public long EntityRevision { get; private set; }
    public bool GameplayPaused { get; set; }
    public bool AnimationPaused { get; set; }
    public Scene Scene { get; set; }
    public Renderer Renderer { get; private set; }
    public SpatialIndex Spatial { get; private set; }

    // Job system
    private readonly Thread[] Workers;
    private readonly ManualResetEventSlim[] JobReadyEvents;
    private readonly ManualResetEventSlim[] JobDoneEvents;
    private volatile bool ShuttingDown;
    /// <summary>Describes an archetype range assigned to an ECS query worker.</summary>
    private struct JobData { public int ThreadIdx, Start, End; }
    private readonly JobData[] JobDataArray;
    private Action<int, int, int> CurrentWork;

    /// <summary>Binds entity storage and system scheduling to a scene and renderer.</summary>
    public ECS(Scene scene, Renderer renderer)
    {
        Scene = scene;
        Renderer = renderer;
        RecycledIds = new Stack<int>();
        Systems = new List<System>();
        SystemCache = new Dictionary<Type, System>();
        Archetypes = new List<Archetype>();
        ArchetypesBySignature = new Dictionary<ulong, Archetype>();
        TypeToId = new Dictionary<Type, int>();
        EntityRecords = new EntityRecord[InitialEntityCapacity];
        Spatial = new SpatialIndex(this, 64f);

        Workers = new Thread[CachedThreadCount];
        JobDataArray = new JobData[CachedThreadCount];
        JobReadyEvents = new ManualResetEventSlim[CachedThreadCount];
        JobDoneEvents = new ManualResetEventSlim[CachedThreadCount];

        InitializeJobSystem();
    }

    /// <summary>Orders registered systems and invokes their initialization callbacks.</summary>
    public void Initialize()
    {
        SortSystemsByDependencies();
        BuildRenderOrder();

        for (int i = 0; i < Systems.Count; i++)
            if (Systems[i].Enabled) Systems[i].Initialize();
    }

    /// <summary>Builds rendering order from system dependencies and render layers.</summary>
    private void BuildRenderOrder()
    {
        RenderSystems = new List<System>(Systems);

        var systemIndex = new Dictionary<System, int>(Systems.Count);

        for (int i = 0; i < Systems.Count; i++)
            systemIndex[Systems[i]] = i;

        RenderSystems.Sort((a, b) =>
        {
            int layerCmp = a.RenderLayer.CompareTo(b.RenderLayer);

            if (layerCmp != 0) return layerCmp;

            return systemIndex[a].CompareTo(systemIndex[b]);
        });
    }

    /// <summary>Orders systems according to declared type and instance dependencies.</summary>
    private void SortSystemsByDependencies()
    {
        if (Systems.Count <= 1) return;

        var typeToIndices = new Dictionary<Type, List<int>>();

        for (int i = 0; i < Systems.Count; i++)
        {
            var type = Systems[i].GetType();
            if (!typeToIndices.TryGetValue(type, out var indices)) typeToIndices[type] = indices = new();
            indices.Add(i);
        }

        var instanceToIndex = new Dictionary<System, int>();

        for (int i = 0; i < Systems.Count; i++) instanceToIndex[Systems[i]] = i;

        var outgoing = new List<int>[Systems.Count];
        var inDegree = new int[Systems.Count];

        for (int i = 0; i < Systems.Count; i++)
            outgoing[i] = new List<int>();

        for (int i = 0; i < Systems.Count; i++)
        {
            foreach (var dependency in Systems[i].After)
            {
                if (!instanceToIndex.TryGetValue(dependency, out int dependencyIndex)) throw new InvalidOperationException("System dependency belongs to another ECS.");

                outgoing[dependencyIndex].Add(i);
                inDegree[i]++;
            }

            var type = Systems[i].GetType();

            foreach (var attr in type.GetCustomAttributes(typeof(RunAfterAttribute), true))
            {
                var runAfter = (RunAfterAttribute)attr;

                foreach (var dep in runAfter.SystemTypes)
                    if (typeToIndices.TryGetValue(dep, out var beforeIndices))
                        foreach (int beforeIdx in beforeIndices)
                        {
                            outgoing[beforeIdx].Add(i);
                            inDegree[i]++;
                        }
            }

            foreach (var attr in type.GetCustomAttributes(typeof(RunBeforeAttribute), true))
            {
                var runBefore = (RunBeforeAttribute)attr;

                foreach (var dep in runBefore.SystemTypes)
                    if (typeToIndices.TryGetValue(dep, out var afterIndices))
                        foreach (int afterIdx in afterIndices)
                        {
                            outgoing[i].Add(afterIdx);
                            inDegree[afterIdx]++;
                        }
            }
        }

        var queue = new Queue<int>();

        for (int i = 0; i < Systems.Count; i++)
            if (inDegree[i] == 0) queue.Enqueue(i);

        var sorted = new List<System>(Systems.Count);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            sorted.Add(Systems[current]);

            foreach (int next in outgoing[current])
                if (--inDegree[next] == 0) queue.Enqueue(next);
        }

        if (sorted.Count != Systems.Count) throw new InvalidOperationException("System dependencies contain a cycle.");

        if (sorted.Count == Systems.Count)
        {
            Systems.Clear();
            Systems.AddRange(sorted);
        }
    }

    /// <summary>Stops query workers and releases registered system resources.</summary>
    public void Dispose()
    {
        ShutdownJobSystem();

        for (int i = 0; i < Systems.Count; i++)
            Systems[i].Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>Attaches a system to this scene and registers it for lifecycle callbacks.</summary>
    public T AddSystem<T>(bool enabled = true) where T : System, new() => AddSystem(new T(), enabled);

    /// <summary>Attaches a system to this scene and registers it for lifecycle callbacks.</summary>
    public T AddSystem<T>(T system, bool enabled = true) where T : System
    {
        system.Scene = Scene;
        system.Renderer = Scene.Renderer;
        system.Time = Scene.Time;
        system.Enabled = enabled;

        var pauseAttr = (PausableAttribute)Attribute.GetCustomAttribute(typeof(T), typeof(PausableAttribute));
        system.PauseGroups = pauseAttr?.Groups ?? PauseGroup.None;
        Systems.Add(system);
        SystemCache[typeof(T)] = system;

        return system;
    }

    /// <summary>Returns the registered system matching the requested type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetSystem<T>() where T : System
    {
        return SystemCache.TryGetValue(typeof(T), out var system) ? (T)system : null;
    }

    /// <summary>Enumerates the systems registered with this ECS.</summary>
    public IReadOnlyList<System> GetAllSystems() => Systems;

    /// <summary>Registers a named group of mutually exclusive systems.</summary>
    public void RegisterSystemGroup(SystemGroup group)
    {
        SystemGroups.Add(group);
        group.ForEach(system => SystemToGroup[system] = group);
    }

    /// <summary>Returns the named system group when it is registered.</summary>
    public SystemGroup GetSystemGroup(System system) =>
        SystemToGroup.TryGetValue(system, out var group) ? group : null;

    /// <summary>Finds the group name associated with a registered system.</summary>
    public string GetGroupName(System system) =>
        SystemToGroup.TryGetValue(system, out var group) ? group.Name : null;

    /// <summary>Assigns or retrieves the component type's signature bit index.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetTypeId(Type type)
    {
        if (!TypeToId.TryGetValue(type, out int id))
        {
            id = NextTypeId++;
            TypeToId[type] = id;
        }

        return id;
    }

    /// <summary>Builds an archetype signature from the supplied component types.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong GetSignature(params Type[] types)
    {
        ulong sig = 0;

        foreach (var type in types)
            sig |= 1UL << GetTypeId(type);

        return sig;
    }

    /// <summary>Finds component storage for a signature or allocates it on first use.</summary>
    private Archetype GetOrCreateArchetype(ulong signature, Type[] types)
    {
        if (!ArchetypesBySignature.TryGetValue(signature, out var arch))
        {
            arch = new Archetype(types, signature);
            Archetypes.Add(arch);
            ArchetypesBySignature[signature] = arch;
        }

        return arch;
    }

    /// <summary>Expands entity-indexed storage to accommodate the requested identifier.</summary>
    private void EnsureEntityCapacity(int entityId)
    {
        if (entityId >= EntityRecords.Length)
        {
            int newSize = EntityRecords.Length;

            while (newSize <= entityId) newSize *= 2;

            Array.Resize(ref EntityRecords, newSize);
        }
    }

    /// <summary>Allocates a live entity ID without adding components or a spatial position.</summary>
    public int CreateEntity()
    {
        int id = RecycledIds.Count > 0 ? RecycledIds.Pop() : NextEntityId++;
        EnsureEntityCapacity(id);
        EntityRecords[id] = default;
        EntityCountValue++;
        EntityRevision++;

        return id;
    }

    /// <summary>Allocates an entity ID and initializes its requested spatial position.</summary>
    public int CreateEntity(Vector2 position, float layer = 0)
    {
        int id = CreateEntity();
        AddComponent<Transform2D>(id) = new Transform2D { Position = position, Layer = layer };
        Spatial.Insert(id, new Vector3(position, layer));

        return id;
    }

    /// <summary>Allocates an entity ID and initializes its requested spatial position.</summary>
    public int CreateEntity(Vector3 position)
    {
        int id = CreateEntity();
        AddComponent<Transform3D>(id) = new Transform3D();

        ref var transform = ref GetComponent<Transform3D>(id);
        transform.Position = position;
        Spatial.Insert(id, position);

        return id;
    }

    /// <summary> Returns true if the entity ID refers to a living entity. </summary>
    public bool IsAlive(int entity) => entity >= 0 && entity < EntityRecords.Length && EntityRecords[entity].Arch != null;

    /// <summary> Schedules an entity for destruction at the start of the next Update. Safe to call mid-frame. </summary>
    public void ScheduleDestroy(int entity) => DeferredDestroyQueue.Add(entity);

    /// <summary> Processes all deferred entity destructions. Called automatically at the start of Update. </summary>
    public void FlushDeferred()
    {
        if (DeferredDestroyQueue.Count == 0) return;

        foreach (int entity in DeferredDestroyQueue)
            DestroyEntity(entity);

        DeferredDestroyQueue.Clear();
    }

    /// <summary> Collects all living entity IDs into the provided list (cleared first). </summary>
    public void GetAllEntityIds(List<int> result)
    {
        result.Clear();

        foreach (var arch in Archetypes)
        {
            var entities = arch.GetEntities();

            for (int i = 0; i < arch.EntityCount; i++)
                result.Add(entities[i]);
        }
    }

    /// <summary>Removes every entity and clears associated component and spatial state.</summary>
    public void DestroyAllEntities()
    {
        EntityRevision++;

        foreach (var arch in Archetypes)
        {
            var entities = arch.GetEntities();

            for (int i = arch.EntityCount - 1; i >= 0; i--)
            {
                int entity = entities[i];
                Spatial.Remove(entity);
                arch.Remove(i);
                EntityRecords[entity] = default;
                RecycledIds.Push(entity);
                EntityCountValue--;
            }
        }

        foreach (var bitset in TagSets.Values)
            bitset.Clear();
    }

    /// <summary>Removes a live entity from component storage, tags, and spatial lookup.</summary>
    public bool DestroyEntity(int entity)
    {
        ref var record = ref EntityRecords[entity];

        if (record.Arch == null) return false;

        Spatial.Remove(entity);

        foreach (var bitset in TagSets.Values)
            bitset.Remove(entity);

        DisabledEntities.Remove(entity);

        int movedEntity = record.Arch.Remove(record.Index);
        if (movedEntity >= 0) EntityRecords[movedEntity].Index = record.Index;

        record.Arch = null;
        record.Index = -1;
        RecycledIds.Push(entity);
        EntityCountValue--;
        EntityRevision++;

        return true;
    }

    /// <summary>Adds a component type to an entity and returns a reference to its storage.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T AddComponent<T>(int entity) where T : struct, Component
    {
        ref var record = ref EntityRecords[entity];
        var oldArch = record.Arch;
        var type = typeof(T);

        // Calculate new signature
        ulong newSig = oldArch != null ? oldArch.Signature | (1UL << GetTypeId(type)) : (1UL << GetTypeId(type));

        if (oldArch != null && oldArch.Signature == newSig) return ref oldArch.Get<T>(record.Index);

        // Only build type array if archetype doesn't exist yet (avoids allocation in common case)
        if (!ArchetypesBySignature.TryGetValue(newSig, out var newArch))
        {
            var types = new List<Type>();
            if (oldArch != null) types.AddRange(oldArch.Types);
            types.Add(type);
            newArch = new Archetype(types.ToArray(), newSig);
            Archetypes.Add(newArch);
            ArchetypesBySignature[newSig] = newArch;
        }

        int newIndex = newArch.Add(entity);
        newArch.Set<T>(newIndex, new T());

        if (oldArch != null)
        {
            newArch.CopyComponentsFrom(oldArch, record.Index, newIndex);

            int movedEntity = oldArch.Remove(record.Index);
            if (movedEntity >= 0) EntityRecords[movedEntity].Index = record.Index;
        }

        record.Arch = newArch;
        record.Index = newIndex;
        EntityRevision++;

        return ref newArch.Get<T>(newIndex);
    }

    /// <summary>Returns a mutable reference to an entity's stored component.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponent<T>(int entity) where T : struct, Component
    {
        ref var record = ref EntityRecords[entity];
        return ref record.Arch.Get<T>(record.Index);
    }

    /// <summary>Reports whether the entity has the requested component type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent<T>(int entity) where T : struct, Component
    {
        ref var record = ref EntityRecords[entity];
        return record.Arch != null && record.Arch.HasComponent<T>();
    }

    /// <summary>Returns the component types currently attached to an entity.</summary>
    public Type[] GetComponentTypes(int entity)
    {
        ref var record = ref EntityRecords[entity];
        return record.Arch?.Types ?? Array.Empty<Type>();
    }

    /// <summary>Returns the component types known to this ECS.</summary>
    public IReadOnlyCollection<Type> GetRegisteredComponentTypes() => TypeToId.Keys;

    /// <summary>Boxed component access for tooling. Runtime queries retain typed, allocation-free access.</summary>
    public bool TryGetComponent(int entity, Type type, out Component component)
    {
        component = null;
        return type != null && IsAlive(entity) && EntityRecords[entity].Arch.TryGetComponent(EntityRecords[entity].Index, type, out component);
    }

    /// <summary>Replaces an existing component when the requested entity or slot supports its type.</summary>
    public bool TrySetComponent(int entity, Component component)
    {
        if (component == null || !IsAlive(entity) || !EntityRecords[entity].Arch.TrySetComponent(EntityRecords[entity].Index, component)) return false;

        if (component is Transform2D transform2D) Spatial.Update(entity, new Vector3(transform2D.Position, transform2D.Layer));
        else if (component is Transform3D transform3D) Spatial.Update(entity, transform3D.Position);
        return true;
    }

    /// <summary>Updates the entity's spatial-index position for subsequent spatial queries.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPosition(int entity, Vector2 position)
    {
        ref var transform = ref GetComponent<Transform2D>(entity);
        transform.Position = position;
        Spatial.Update(entity, new Vector3(position, transform.Layer));
    }

    /// <summary>Updates the entity's spatial-index position for subsequent spatial queries.</summary>
    public void SetPosition(int entity, Vector3 position)
    {
        GetComponent<Transform3D>(entity).Position = position;
        Spatial.Update(entity, position);
    }

    /// <summary>Collects entities within the requested distance of a world-space position.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InRadius(Vector3 center, float radius) => Spatial.InRadius(center, radius);

    /// <summary>Collects entities within the requested distance of a world-space position.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InRadius(float x, float y, float z, float radius) => Spatial.InRadius(x, y, z, radius);

    /// <summary>Collects entities inside the supplied world-space bounds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InBox(Vector3 min, Vector3 max) => Spatial.InBox(min, max);

    /// <summary>Looks up an entity at the exact indexed coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(Vector3 position) => Spatial.AtExact(position);

    /// <summary>Looks up an entity at the exact indexed coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(float x, float y, float z) => Spatial.AtExact(x, y, z);

    /// <summary>Finds nearby entities ordered by distance from the query position.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> Nearest(Vector3 center, int count, float maxRadius = float.MaxValue) => Spatial.Nearest(center, count, maxRadius);

    /// <summary>Finds nearby entities ordered by distance from the query position.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> Nearest(float cx, float cy, float cz, int count, float maxRadius = float.MaxValue) => Spatial.Nearest(cx, cy, cz, count, maxRadius);

    /// <summary>Adds an entity to the named tag's membership set.</summary>
    public void AddTag(int entity, string tag)
    {
        if (!TagSets.TryGetValue(tag, out var bitset))
        {
            bitset = new PagedBitSet();
            TagSets[tag] = bitset;
        }

        bitset.Add(entity);
    }

    /// <summary>Removes an entity from the named tag's membership set.</summary>
    public void RemoveTag(int entity, string tag)
    {
        if (TagSets.TryGetValue(tag, out var bitset)) bitset.Remove(entity);
    }

    /// <summary>Reports whether an entity belongs to the named tag.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasTag(int entity, string tag) => TagSets.TryGetValue(tag, out var bitset) && bitset.Contains(entity);

    /// <summary>
    /// Returns the PagedBitSet for a tag. Caller iterates directly via foreach (zero-copy).
    /// Returns null if the tag has never been used.
    /// </summary>
    public PagedBitSet WithTag(string tag) => TagSets.TryGetValue(tag, out var bitset) ? bitset : null;

    /// <summary>Destroys every live entity in the named tag's membership set.</summary>
    public void DestroyEntitiesWithTag(string tag)
    {
        if (!TagSets.TryGetValue(tag, out var bitset)) return;

        foreach (int entity in bitset)
            DestroyEntity(entity);

        bitset.Clear();
    }

    /// <summary>Clears the named tag's membership without destroying its entities.</summary>
    public void ClearTag(string tag)
    {
        if (TagSets.TryGetValue(tag, out var bitset)) bitset.Clear();
    }

    /// <summary> Disables an entity. Disabled entities are skipped by Query but remain alive and restorable. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DisableEntity(int entity) { if (DisabledEntities.Add(entity)) EntityRevision++; }

    /// <summary> Re-enables a previously disabled entity so it participates in queries again. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnableEntity(int entity) { if (DisabledEntities.Remove(entity)) EntityRevision++; }

    /// <summary> Returns true if the entity is currently disabled. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDisabled(int entity) => DisabledEntities.Contains(entity);

    public static int ThreadCount => CachedThreadCount;

    /// <summary>Creates workers and synchronization state for parallel component queries.</summary>
    private void InitializeJobSystem()
    {
        for (int i = 0; i < CachedThreadCount; i++)
        {
            int idx = i;
            JobReadyEvents[i] = new ManualResetEventSlim(false);
            JobDoneEvents[i] = new ManualResetEventSlim(true);
            Workers[i] = new Thread(() => WorkerLoop(idx)) { IsBackground = true, Name = $"ECS Worker {idx}" };
            Workers[i].Start();
        }
    }

    /// <summary>Processes assigned component-query ranges until shutdown is requested.</summary>
    private void WorkerLoop(int idx)
    {
        while (!ShuttingDown)
        {
            JobReadyEvents[idx].Wait();

            if (ShuttingDown) return;

            JobReadyEvents[idx].Reset();

            ref var data = ref JobDataArray[idx];
            CurrentWork?.Invoke(data.ThreadIdx, data.Start, data.End);
            JobDoneEvents[idx].Set();
        }
    }

    /// <summary>Distributes matching archetype ranges across query workers and waits for completion.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RunParallel(int count, Action<int, int, int> work)
    {
        if (count == 0) return;

        CurrentWork = work;

        int chunkSize = (count + CachedThreadCount - 1) / CachedThreadCount;

        for (int i = 0; i < CachedThreadCount; i++)
        {
            int start = i * chunkSize;
            int end = Math.Min(start + chunkSize, count);

            if (start >= count) { JobDoneEvents[i].Set(); continue; }

            JobDataArray[i] = new JobData { ThreadIdx = i, Start = start, End = end };
            JobDoneEvents[i].Reset();
            JobReadyEvents[i].Set();
        }

        for (int i = 0; i < CachedThreadCount; i++)
            JobDoneEvents[i].Wait();
    }

    /// <summary>Signals query workers to stop and waits for them to exit.</summary>
    private void ShutdownJobSystem()
    {
        ShuttingDown = true;

        for (int i = 0; i < CachedThreadCount; i++)
        {
            JobReadyEvents[i]?.Set();
            Workers[i]?.Join(100);
            JobReadyEvents[i]?.Dispose();
            JobDoneEvents[i]?.Dispose();
        }
    }

    /// <summary>Callback receiving worker and entity identifiers plus mutable component references.</summary>
    public delegate void QueryAction<T1>(int threadIndex, int entity, ref T1 c1) where T1 : struct;
    /// <summary>Callback receiving worker and entity identifiers plus mutable component references.</summary>
    public delegate void QueryAction<T1, T2>(int threadIndex, int entity, ref T1 c1, ref T2 c2) where T1 : struct where T2 : struct;
    /// <summary>Callback receiving worker and entity identifiers plus mutable component references.</summary>
    public delegate void QueryAction<T1, T2, T3>(int threadIndex, int entity, ref T1 c1, ref T2 c2, ref T3 c3) where T1 : struct where T2 : struct where T3 : struct;

    /// <summary>Visits matching entities with mutable references to the requested components.</summary>
    public void Query<T1>(QueryAction<T1> action) where T1 : struct, Component
    {
        ulong sig = 1UL << GetTypeId(typeof(T1));

        QueryMatchCache.Clear();

        int totalCount = 0;

        foreach (var arch in Archetypes)
        {
            if ((arch.Signature & sig) != sig) continue;
            if (arch.EntityCount == 0) continue;

            QueryMatchCache.Add(arch);
            totalCount += arch.EntityCount;
        }

        if (totalCount == 0) return;

        var disabled = DisabledEntities;

        if (QueryMatchCache.Count == 1)
        {
            var arch = QueryMatchCache[0];
            var entities = arch.GetEntities();
            var data1 = arch.GetArray<T1>();
            RunParallel(arch.EntityCount, (threadIdx, start, end) =>
            {
                for (int i = start; i < end; i++)
                {
                    if (disabled.Contains(entities[i])) continue;

                    action(threadIdx, entities[i], ref data1[i]);
                }
            });

            return;
        }

        RunParallel(totalCount, (threadIdx, start, end) =>
        {
            int globalIdx = 0;

            foreach (var arch in QueryMatchCache)
            {
                int archCount = arch.EntityCount;
                int archEnd = globalIdx + archCount;

                if (start >= archEnd) { globalIdx = archEnd; continue; }
                if (end <= globalIdx) break;

                int localStart = Math.Max(0, start - globalIdx);
                int localEnd = Math.Min(archCount, end - globalIdx);
                var entities = arch.GetEntities();
                var data1 = arch.GetArray<T1>();

                for (int i = localStart; i < localEnd; i++)
                {
                    if (disabled.Contains(entities[i])) continue;

                    action(threadIdx, entities[i], ref data1[i]);
                }

                globalIdx = archEnd;
            }
        });
    }

    /// <summary>Visits matching entities with mutable references to the requested components.</summary>
    public void Query<T1, T2>(QueryAction<T1, T2> action)
        where T1 : struct, Component
        where T2 : struct, Component
    {
        ulong sig = (1UL << GetTypeId(typeof(T1))) | (1UL << GetTypeId(typeof(T2)));

        QueryMatchCache.Clear();

        int totalCount = 0;

        foreach (var arch in Archetypes)
        {
            if ((arch.Signature & sig) != sig) continue;
            if (arch.EntityCount == 0) continue;

            QueryMatchCache.Add(arch);
            totalCount += arch.EntityCount;
        }

        if (totalCount == 0) return;

        var disabled = DisabledEntities;

        if (QueryMatchCache.Count == 1)
        {
            var arch = QueryMatchCache[0];
            var entities = arch.GetEntities();
            var data1 = arch.GetArray<T1>();
            var data2 = arch.GetArray<T2>();
            RunParallel(arch.EntityCount, (threadIdx, start, end) =>
            {
                for (int i = start; i < end; i++)
                {
                    if (disabled.Contains(entities[i])) continue;

                    action(threadIdx, entities[i], ref data1[i], ref data2[i]);
                }
            });

            return;
        }

        RunParallel(totalCount, (threadIdx, start, end) =>
        {
            int globalIdx = 0;

            foreach (var arch in QueryMatchCache)
            {
                int archCount = arch.EntityCount;
                int archEnd = globalIdx + archCount;

                if (start >= archEnd) { globalIdx = archEnd; continue; }
                if (end <= globalIdx) break;

                int localStart = Math.Max(0, start - globalIdx);
                int localEnd = Math.Min(archCount, end - globalIdx);
                var entities = arch.GetEntities();
                var data1 = arch.GetArray<T1>();
                var data2 = arch.GetArray<T2>();

                for (int i = localStart; i < localEnd; i++)
                {
                    if (disabled.Contains(entities[i])) continue;

                    action(threadIdx, entities[i], ref data1[i], ref data2[i]);
                }

                globalIdx = archEnd;
            }
        });
    }

    // Cached archetype query results
    private readonly List<Archetype> QueryMatchCache = new();

    /// <summary>Visits matching entities with mutable references to the requested components.</summary>
    public void Query<T1, T2, T3>(QueryAction<T1, T2, T3> action)
        where T1 : struct, Component
        where T2 : struct, Component
        where T3 : struct, Component
    {
        ulong sig = (1UL << GetTypeId(typeof(T1))) | (1UL << GetTypeId(typeof(T2))) | (1UL << GetTypeId(typeof(T3)));

        // Find all matching archetypes and total count
        QueryMatchCache.Clear();

        int totalCount = 0;

        foreach (var arch in Archetypes)
        {
            if ((arch.Signature & sig) != sig) continue;
            if (arch.EntityCount == 0) continue;

            QueryMatchCache.Add(arch);
            totalCount += arch.EntityCount;
        }

        if (totalCount == 0) return;

        var disabled = DisabledEntities;

        // Single archetype - fast path
        if (QueryMatchCache.Count == 1)
        {
            var arch = QueryMatchCache[0];
            var entities = arch.GetEntities();
            var data1 = arch.GetArray<T1>();
            var data2 = arch.GetArray<T2>();
            var data3 = arch.GetArray<T3>();
            int count = arch.EntityCount;

            RunParallel(count, (threadIdx, start, end) =>
            {
                for (int i = start; i < end; i++)
                {
                    if (disabled.Contains(entities[i])) continue;

                    action(threadIdx, entities[i], ref data1[i], ref data2[i], ref data3[i]);
                }
            });

            return;
        }

        // Multiple archetypes - iterate with global index
        RunParallel(totalCount, (threadIdx, start, end) =>
        {
            int globalIdx = 0;

            foreach (var arch in QueryMatchCache)
            {
                int archCount = arch.EntityCount;
                int archEnd = globalIdx + archCount;

                if (start >= archEnd) { globalIdx = archEnd; continue; }
                if (end <= globalIdx) break;

                int localStart = Math.Max(0, start - globalIdx);
                int localEnd = Math.Min(archCount, end - globalIdx);

                var entities = arch.GetEntities();
                var data1 = arch.GetArray<T1>();
                var data2 = arch.GetArray<T2>();
                var data3 = arch.GetArray<T3>();

                for (int i = localStart; i < localEnd; i++)
                {
                    if (disabled.Contains(entities[i])) continue;

                    action(threadIdx, entities[i], ref data1[i], ref data2[i], ref data3[i]);
                }

                globalIdx = archEnd;
            }
        });
    }

    /// <summary>Updates enabled, unpaused systems after applying deferred entity destruction and resizing.</summary>
    public void Update()
    {
        FlushDeferred();

        if (Renderer != null && Renderer.HasPendingResize)
        {
            Renderer.HandleResize();

            for (int i = 0; i < Systems.Count; i++)
                if (Systems[i].Enabled) Systems[i].OnResize();
        }

        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            if (IsSystemPaused(Systems[i])) continue;

            Systems[i].Time = Scene.Time;
            Systems[i].Update();
        }
    }

    /// <summary>Runs fixed-timestep callbacks on enabled, unpaused systems.</summary>
    public void FixedUpdate()
    {
        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;
            if (IsSystemPaused(Systems[i])) continue;

            Systems[i].Time = Scene.Time;
            Systems[i].FixedUpdate();
        }
    }

    /// <summary>Reports whether any of the system's pause groups is currently paused.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsSystemPaused(System system)
    {
        var groups = system.PauseGroups;

        if (groups == PauseGroup.None) return false;
        if (GameplayPaused && (groups & PauseGroup.Gameplay) != 0) return true;
        if (AnimationPaused && (groups & PauseGroup.Animation) != 0) return true;

        return false;
    }

    /// <summary>Draws enabled systems in dependency and render-layer order.</summary>
    public void Render()
    {
        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;

            Systems[i].Time = Scene.Time;
            Systems[i].Render();
        }
    }

    /// <summary>Runs enabled systems' overlay callbacks after the main render pass.</summary>
    public void LateRender()
    {
        for (int i = 0; i < RenderSystems.Count; i++)
        {
            if (!RenderSystems[i].Enabled) continue;

            RenderSystems[i].Time = Scene.Time;
            RenderSystems[i].LateRender();
        }
    }
}
