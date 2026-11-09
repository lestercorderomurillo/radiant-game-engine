using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace com.radiant.engine.core;

/// <summary>
/// Archetype stores entities with identical component combinations.
/// Components are stored in contiguous arrays for cache-friendly iteration.
/// </summary>
public sealed class Archetype
{
    public readonly Type[] Types;
    public readonly ulong Signature;

    private readonly Dictionary<Type, Array> Components;
    private int[] Entities;
    private int Count;
    private int Capacity;

    private const int InitialCapacity = 64;

    public int EntityCount => Count;

    /// <summary>Allocates component arrays for the supplied type signature.</summary>
    public Archetype(Type[] types, ulong signature)
    {
        Types = types;
        Signature = signature;
        Components = new Dictionary<Type, Array>(types.Length);
        Entities = new int[InitialCapacity];
        Capacity = InitialCapacity;

        foreach (var type in types)
            Components[type] = Array.CreateInstance(type, InitialCapacity);
    }

    /// <summary>Reports whether this archetype stores the requested component type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent<T>() where T : struct => Components.ContainsKey(typeof(T));

    /// <summary>Returns the contiguous storage array for a component type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T[] GetArray<T>() where T : struct => (T[])Components[typeof(T)];

    /// <summary>Reads a component from an occupied slot, returning false if its type is absent.</summary>
    internal bool TryGetComponent(int index, Type type, out Component component)
    {
        component = Components.TryGetValue(type, out var values) ? (Component)values.GetValue(index) : null;
        return component != null;
    }

    /// <summary>Replaces an existing component when the requested entity or slot supports its type.</summary>
    internal bool TrySetComponent(int index, Component component)
    {
        if (!Components.TryGetValue(component.GetType(), out var values)) return false;

        values.SetValue(component, index);
        return true;
    }

    /// <summary>Returns the entity-ID array; only entries below EntityCount are occupied.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int[] GetEntities() => Entities;

    /// <summary>Appends an entity and returns its new component-array slot.</summary>
    public int Add(int entity)
    {
        if (Count >= Capacity) Grow();

        int index = Count++;
        Entities[index] = entity;

        return index;
    }

    /// <summary>Writes a component into the specified occupied slot.</summary>
    public void Set<T>(int index, in T value) where T : struct
    {
        ((T[])Components[typeof(T)])[index] = value;
    }

    /// <summary>Returns a mutable reference to a component at the specified slot.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(int index) where T : struct
    {
        return ref ((T[])Components[typeof(T)])[index];
    }

    /// <summary>Removes a slot and returns the entity moved into it, or -1 when no entity moved.</summary>
    public int Remove(int index)
    {
        int lastIndex = --Count;

        if (index != lastIndex)
        {
            // Swap with last
            Entities[index] = Entities[lastIndex];

            foreach (var kvp in Components)
            {
                Array.Copy(kvp.Value, lastIndex, kvp.Value, index, 1);
            }
        }

        return lastIndex != index ? Entities[index] : -1; // Return moved entity or -1
    }

    /// <summary>Expands entity and component storage while retaining existing data.</summary>
    private void Grow()
    {
        int newCapacity = Capacity * 2;

        Array.Resize(ref Entities, newCapacity);

        foreach (var type in Types)
        {
            var oldArray = Components[type];
            var newArray = Array.CreateInstance(type, newCapacity);
            Array.Copy(oldArray, newArray, Count);
            Components[type] = newArray;
        }

        Capacity = newCapacity;
    }

    /// <summary>Copies component values shared by the source and destination archetypes.</summary>
    public void CopyComponentsFrom(Archetype source, int sourceIndex, int destIndex)
    {
        foreach (var type in Types)
        {
            if (source.Components.TryGetValue(type, out var srcArray)) Array.Copy(srcArray, sourceIndex, Components[type], destIndex, 1);
        }
    }
}
