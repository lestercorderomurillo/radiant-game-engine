using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using com.radiant.engine.bundle;

namespace com.radiant.engine.core;

/// <summary>
/// Holds a scene's entities, their components and its systems. Create entities, attach plain-data
/// components, and run fast parallel queries over everything that matches.
/// </summary>
/// <example>
/// The following example shows how to create a glowing ball and move every ball each fixed tick:
/// <code>
/// int ball = ECS.CreateEntity(new Vector2(400, 300));
/// ECS.AddComponent&lt;Circle2D&gt;(ball) = new Circle2D(16f);
/// ECS.AddComponent&lt;Movement2D&gt;(ball);
/// ECS.AddComponent&lt;Material&gt;(ball).Emissive = Color.Orange;
///
/// ECS.Query&lt;Transform2D, Movement2D&gt;((int thread, int entity, ref Transform2D transform, ref Movement2D movement) =>
///     transform.Position += movement.Speed * Time.DeltaSeconds);
/// </code>
/// </example>
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

    /// <summary>Number of living entities.</summary>
    public int EntityCount => EntityCountValue;
    /// <summary>Changes whenever entities are created, destroyed, enabled, disabled or gain or lose components. Compare it between frames to refresh cached lists cheaply.</summary>
    public long EntityRevision { get; private set; }
    /// <summary>Pauses every system marked <c>[Pausable]</c>, such as movement and AI, while rendering and UI keep running.</summary>
    public bool GameplayPaused { get; set; }
    /// <summary>Pauses systems marked <c>[Pausable(PauseGroup.Animation)]</c>, such as orbiting lights, independently of gameplay.</summary>
    public bool AnimationPaused { get; set; }
    /// <summary>The scene this ECS belongs to.</summary>
    public Scene Scene { get; set; }
    /// <summary>The renderer shared by the scene's systems.</summary>
    public IRenderer Renderer { get; private set; }
    /// <summary>The spatial index behind <c>InRadius</c>, <c>InBox</c>, <c>Nearest</c> and <c>AtExact</c>.</summary>
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

    /// <summary>Creates an ECS for a scene. Scenes create their own; you only need this for standalone tools and tests.</summary>
    public ECS(Scene scene, IRenderer renderer)
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

    /// <summary>True once the registered systems have been sorted and initialized.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>Sorts the registered systems by their dependencies and calls their <c>Initialize</c>. The game loop calls this for you after <c>Scene.SetupECS</c>; later calls do nothing.</summary>
    public void Initialize()
    {
        if (IsInitialized) return;
        IsInitialized = true;
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

    /// <summary>Disposes every system and stops the worker threads.</summary>
    public void Dispose()
    {
        ShutdownJobSystem();

        for (int i = 0; i < Systems.Count; i++)
            Systems[i].Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>Adds a system to the scene and returns it so you can configure it. Order doesn't matter; dependencies sort it.</summary>
    /// <param name="enabled">Pass false to register a system that a <c>SystemGroup</c> will switch on later.</param>
    /// <example>
    /// The following example shows how to register systems in <c>SetupECS</c>:
    /// <code>
    /// public override void SetupECS()
    /// {
    ///     ECS.AddSystem&lt;Geometry&gt;();
    ///     var lighting = ECS.AddSystem&lt;HRCGI&gt;();
    ///     lighting.OutputJitter = Vector2.Zero;
    /// }
    /// </code>
    /// </example>
    public T AddSystem<T>(bool enabled = true) where T : System, new() => AddSystem(new T(), enabled);

    /// <summary>Adds a system instance you created yourself, for example one with constructor arguments.</summary>
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

    /// <summary>Returns the registered system of a type, or null if the scene doesn't have one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetSystem<T>() where T : System
    {
        return SystemCache.TryGetValue(typeof(T), out var system) ? (T)system : null;
    }

    /// <summary>Every registered system, in execution order.</summary>
    public IReadOnlyList<System> GetAllSystems() => Systems;

    /// <summary>Registers a group of interchangeable systems so the System Inspector shows them together.</summary>
    public void RegisterSystemGroup(SystemGroup group)
    {
        SystemGroups.Add(group);
        group.ForEach(system => SystemToGroup[system] = group);
    }

    /// <summary>Returns the group a system belongs to, or null.</summary>
    public SystemGroup GetSystemGroup(System system) =>
        SystemToGroup.TryGetValue(system, out var group) ? group : null;

    /// <summary>Returns the name of the group a system belongs to, or null.</summary>
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

    /// <summary>Creates an empty entity and returns its ID.</summary>
    public int CreateEntity()
    {
        int id = RecycledIds.Count > 0 ? RecycledIds.Pop() : NextEntityId++;
        EnsureEntityCapacity(id);
        EntityRecords[id] = default;
        EntityCountValue++;
        EntityRevision++;

        return id;
    }

    /// <summary>Creates a 2D entity with a <c>Transform2D</c> at a position and draw layer, ready for spatial queries.</summary>
    public int CreateEntity(Vector2 position, float layer = 0)
    {
        int id = CreateEntity();
        AddComponent<Transform2D>(id) = new Transform2D { Position = position, Layer = layer };
        Spatial.Insert(id, new Vector3(position, layer));

        return id;
    }

    /// <summary>Creates a 3D entity with a <c>Transform3D</c> at a position, ready for spatial queries.</summary>
    public int CreateEntity(Vector3 position)
    {
        int id = CreateEntity();
        AddComponent<Transform3D>(id) = new Transform3D();

        ref var transform = ref GetComponent<Transform3D>(id);
        transform.Position = position;
        Spatial.Insert(id, position);

        return id;
    }

    /// <summary>True while the entity exists. IDs are reused after destruction, so don't hold on to stale IDs.</summary>
    public bool IsAlive(int entity) => entity >= 0 && entity < EntityRecords.Length && EntityRecords[entity].Arch != null;

    /// <summary>Destroys an entity at the start of the next update. Safe to call from inside queries and callbacks.</summary>
    public void ScheduleDestroy(int entity) => DeferredDestroyQueue.Add(entity);

    /// <summary>Destroys every entity queued with <c>ScheduleDestroy</c> now. <c>Update</c> calls this automatically.</summary>
    public void FlushDeferred()
    {
        if (DeferredDestroyQueue.Count == 0) return;

        foreach (int entity in DeferredDestroyQueue)
            DestroyEntity(entity);

        DeferredDestroyQueue.Clear();
    }

    /// <summary>Fills a list with every living entity ID (the list is cleared first).</summary>
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

    /// <summary>Destroys every entity, for example when restarting a level.</summary>
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

    /// <summary>Destroys an entity immediately and returns false if it was already gone. Inside queries, use <c>ScheduleDestroy</c> instead.</summary>
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

    /// <summary>Adds a component and returns a reference you can assign or modify in place.</summary>
    /// <remarks>Adding a component moves the entity in memory, which invalidates references you took earlier. Add everything first, then take references.</remarks>
    /// <example>
    /// The following example shows how to add a rectangle and set its material:
    /// <code>
    /// ECS.AddComponent&lt;Rectangle2D&gt;(wall) = new Rectangle2D(new Vector2(200, 20));
    /// ref var material = ref ECS.AddComponent&lt;Material&gt;(wall);
    /// material.Albedo = Color.Gray;
    /// </code>
    /// </example>
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

    /// <summary>Returns a reference to a component so you can read or change it in place.</summary>
    /// <example>
    /// The following example shows how to recolor a light:
    /// <code>
    /// ref var material = ref ECS.GetComponent&lt;Material&gt;(light);
    /// material.Emissive = new Color(120, 180, 255);
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponent<T>(int entity) where T : struct, Component
    {
        ref var record = ref EntityRecords[entity];
        return ref record.Arch.Get<T>(record.Index);
    }

    /// <summary>True if the entity has a component of this type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent<T>(int entity) where T : struct, Component
    {
        ref var record = ref EntityRecords[entity];
        return record.Arch != null && record.Arch.HasComponent<T>();
    }

    /// <summary>Lists the component types on an entity. Meant for tools and inspectors.</summary>
    public Type[] GetComponentTypes(int entity)
    {
        ref var record = ref EntityRecords[entity];
        return record.Arch?.Types ?? Array.Empty<Type>();
    }

    /// <summary>Every component type used in this scene so far.</summary>
    public IReadOnlyCollection<Type> GetRegisteredComponentTypes() => TypeToId.Keys;

    /// <summary>Reads a component by runtime type, boxed. Meant for editors; game code should use <c>GetComponent</c>.</summary>
    public bool TryGetComponent(int entity, Type type, out Component component)
    {
        component = null;
        return type != null && IsAlive(entity) && EntityRecords[entity].Arch.TryGetComponent(EntityRecords[entity].Index, type, out component);
    }

    /// <summary>Replaces a component the entity already has, boxed. Meant for editors; game code should use <c>GetComponent</c>.</summary>
    public bool TrySetComponent(int entity, Component component)
    {
        if (component == null || !IsAlive(entity) || !EntityRecords[entity].Arch.TrySetComponent(EntityRecords[entity].Index, component)) return false;

        if (component is Transform2D transform2D) Spatial.Update(entity, new Vector3(transform2D.Position, transform2D.Layer));
        else if (component is Transform3D transform3D) Spatial.Update(entity, transform3D.Position);
        return true;
    }

    /// <summary>Moves a 2D entity and keeps spatial queries up to date.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPosition(int entity, Vector2 position)
    {
        ref var transform = ref GetComponent<Transform2D>(entity);
        transform.Position = position;
        Spatial.Update(entity, new Vector3(position, transform.Layer));
    }

    /// <summary>Moves an entity and keeps spatial queries up to date.</summary>
    public void SetPosition(int entity, Vector3 position)
    {
        GetComponent<Transform3D>(entity).Position = position;
        Spatial.Update(entity, position);
    }

    /// <summary>Finds entities within a radius, for example everything an explosion hits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InRadius(Vector3 center, float radius) => Spatial.InRadius(center, radius);

    /// <summary>Finds entities within a radius of a point.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InRadius(float x, float y, float z, float radius) => Spatial.InRadius(x, y, z, radius);

    /// <summary>Finds entities inside an axis-aligned box, for example a selection rectangle.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> InBox(Vector3 min, Vector3 max) => Spatial.InBox(min, max);

    /// <summary>Returns the entity at exactly this position, or null. Useful for grids and tile maps.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(Vector3 position) => Spatial.AtExact(position);

    /// <summary>Returns the entity at exactly this position, or null.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int? AtExact(float x, float y, float z) => Spatial.AtExact(x, y, z);

    /// <summary>Finds up to <paramref name="count"/> entities closest to a point, nearest first.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> Nearest(Vector3 center, int count, float maxRadius = float.MaxValue) => Spatial.Nearest(center, count, maxRadius);

    /// <summary>Finds up to <paramref name="count"/> entities closest to a point, nearest first.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> Nearest(float cx, float cy, float cz, int count, float maxRadius = float.MaxValue) => Spatial.Nearest(cx, cy, cz, count, maxRadius);

    /// <summary>Labels an entity with a tag, such as <c>"enemy"</c>. Tags are cheap and are removed automatically when the entity is destroyed.</summary>
    public void AddTag(int entity, string tag)
    {
        if (!TagSets.TryGetValue(tag, out var bitset))
        {
            bitset = new PagedBitSet();
            TagSets[tag] = bitset;
        }

        bitset.Add(entity);
    }

    /// <summary>Removes a tag from an entity.</summary>
    public void RemoveTag(int entity, string tag)
    {
        if (TagSets.TryGetValue(tag, out var bitset)) bitset.Remove(entity);
    }

    /// <summary>True if the entity has the tag.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasTag(int entity, string tag) => TagSets.TryGetValue(tag, out var bitset) && bitset.Contains(entity);

    /// <summary>Returns every entity with a tag, ready to iterate with <c>foreach</c>, or null if the tag was never used.</summary>
    /// <example>
    /// The following example shows how to freeze every enemy:
    /// <code>
    /// if (ECS.WithTag("enemy") is { } enemies)
    ///     foreach (int enemy in enemies) ECS.DisableEntity(enemy);
    /// </code>
    /// </example>
    public PagedBitSet WithTag(string tag) => TagSets.TryGetValue(tag, out var bitset) ? bitset : null;

    /// <summary>Destroys every entity with a tag, for example all projectiles.</summary>
    public void DestroyEntitiesWithTag(string tag)
    {
        if (!TagSets.TryGetValue(tag, out var bitset)) return;

        foreach (int entity in bitset)
            DestroyEntity(entity);

        bitset.Clear();
    }

    /// <summary>Removes a tag from every entity without destroying them.</summary>
    public void ClearTag(string tag)
    {
        if (TagSets.TryGetValue(tag, out var bitset)) bitset.Clear();
    }

    /// <summary>Hides an entity from queries without destroying it. Bring it back with <c>EnableEntity</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DisableEntity(int entity) { if (DisabledEntities.Add(entity)) EntityRevision++; }

    /// <summary>Makes a disabled entity visible to queries again.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnableEntity(int entity) { if (DisabledEntities.Remove(entity)) EntityRevision++; }

    /// <summary>True while the entity is disabled.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDisabled(int entity) => DisabledEntities.Contains(entity);

    /// <summary>Number of worker threads queries run on. Size per-thread buffers with it.</summary>
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

    /// <summary>Called for each matching entity with the worker index, the entity ID and a reference to its component.</summary>
    public delegate void QueryAction<T1>(int threadIndex, int entity, ref T1 c1) where T1 : struct;
    /// <summary>Called for each matching entity with the worker index, the entity ID and references to its two components.</summary>
    public delegate void QueryAction<T1, T2>(int threadIndex, int entity, ref T1 c1, ref T2 c2) where T1 : struct where T2 : struct;
    /// <summary>Called for each matching entity with the worker index, the entity ID and references to its three components.</summary>
    public delegate void QueryAction<T1, T2, T3>(int threadIndex, int entity, ref T1 c1, ref T2 c2, ref T3 c3) where T1 : struct where T2 : struct where T3 : struct;

    /// <summary>Runs your code on every enabled entity that has the component, in parallel across worker threads.</summary>
    /// <remarks>The callback runs on several threads at once: change only the components you receive, or write to per-thread storage indexed by the thread argument.</remarks>
    /// <example>
    /// The following example shows how to make every light flicker:
    /// <code>
    /// float flicker = 0.9f + 0.1f * MathF.Sin((float)Time.Total.TotalSeconds * 20f);
    /// ECS.Query&lt;Material&gt;((int thread, int entity, ref Material material) =>
    ///     material.Emissive = new Color(255, 200, 120) * flicker);
    /// </code>
    /// </example>
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

    /// <summary>Runs your code on every enabled entity that has both components, in parallel.</summary>
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

    /// <summary>Runs your code on every enabled entity that has all three components, in parallel.</summary>
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

    /// <summary>Runs every enabled system's <c>Update</c>. The game loop calls this; you don't need to.</summary>
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

    /// <summary>Runs every enabled system's <c>FixedUpdate</c>. The game loop calls this 64 times per second.</summary>
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

    /// <summary>Runs every enabled system's <c>Render</c> in dependency order. The game loop calls this.</summary>
    public void Render()
    {
        for (int i = 0; i < Systems.Count; i++)
        {
            if (!Systems[i].Enabled) continue;

            Systems[i].Time = Scene.Time;
            Systems[i].Render();
        }
    }

    /// <summary>Runs every enabled system's <c>LateRender</c> by render layer. The game loop calls this.</summary>
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
