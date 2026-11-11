using System;

namespace com.radiant.engine.core;

/// <summary>A reusable recipe that registers fresh systems in each scene's ECS.</summary>
/// <remarks>Register during SetupECS. Each collection is applied once per ECS; systems are never shared between scenes.</remarks>
public sealed class SystemCollection
{
    public string Name { get; }
    private readonly Action<ECS> Configure;

    public SystemCollection(string name, Action<ECS> configure)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A collection needs a name.", nameof(name));
        Name = name;
        Configure = configure ?? throw new ArgumentNullException(nameof(configure));
    }

    internal void Register(ECS ecs) => Configure(ecs);
}
