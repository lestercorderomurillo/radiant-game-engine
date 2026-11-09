using System;

namespace com.radiant.engine.core;

/// <summary>Marker interface for data stored on ECS entities.</summary>
public interface Component { }

/// <summary>Provides a human-readable component description for the inspector.</summary>
[AttributeUsage(AttributeTargets.Struct)]
public class ComponentDescriptionAttribute : Attribute
{
    public string Description { get; }

    /// <summary>Stores the component description shown by inspection tools.</summary>
    public ComponentDescriptionAttribute(string description) => Description = description;
}
