using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Reusable system collections for scene setup.</summary>
public static class SystemCollections
{
    /// <summary>Inspector, entity/component/system editors, logs, profiling, camera controls and gizmos.</summary>
    /// <remarks>Reuses systems already registered by the scene. Inspector discovers 2D and 3D cameras automatically.</remarks>
    public static SystemCollection Debugging { get; } = new("Debugging", ecs =>
    {
        if (ecs.GetSystem<Inspector>() == null) ecs.AddSystem<Inspector>();
        if (ecs.GetSystem<PerformanceMonitor>() == null) ecs.AddSystem<PerformanceMonitor>();
        if (ecs.GetSystem<GizmosRenderer>() == null) ecs.AddSystem<GizmosRenderer>();
    });
}
