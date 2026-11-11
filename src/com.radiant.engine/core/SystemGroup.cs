using System;

namespace com.radiant.engine.core;

/// <summary>
/// A set of interchangeable systems where at most one runs at a time, such as alternative upscalers. Switching
/// disposes the old system and initializes the new one.
/// </summary>
/// <example>
/// The following example shows how to offer two upscalers and cycle them with a key:
/// <code>
/// var upscaling = new SystemGroup("Upscaling",
///     ("Bilinear", ECS.AddSystem&lt;BilinearUpscaler&gt;()),
///     ("UDR3", ECS.AddSystem&lt;UDR3&gt;(enabled: false)));
/// ECS.RegisterSystemGroup(upscaling);
///
/// if (Controls.WasPressed(Key.F11)) upscaling.Toggle();
/// </code>
/// </example>
public class SystemGroup
{
    private readonly (string Name, System System)[] Entries;
    private int ActiveIndex;

    /// <summary>Name shown in the System Inspector, such as <c>"Upscaling"</c>.</summary>
    public string Name { get; }
    /// <summary>Name of the running system, or null when none is.</summary>
    public string ActiveName => ActiveIndex >= 0 ? Entries[ActiveIndex].Name : null;
    /// <summary>The running system, or null when none is.</summary>
    public System Active => ActiveIndex >= 0 ? Entries[ActiveIndex].System : null;
    /// <summary>Index of the running system, or -1 when none is.</summary>
    public int ActiveIdx => ActiveIndex;
    /// <summary>Every entry's name, in order. Handy for a dropdown.</summary>
    public string[] Names => Array.ConvertAll(Entries, e => e.Name);

    /// <summary>Creates a group from named systems. The first enabled entry starts active; register the others with <c>enabled: false</c>.</summary>
    public SystemGroup(string name, params (string name, System system)[] entries)
    {
        Name = name;
        Entries = entries;
        ActiveIndex = Array.FindIndex(entries, e => e.system.Enabled);
        if (ActiveIndex < 0) ActiveIndex = 0;
    }

    /// <summary>Switches to the next system. After the last one, everything is off; toggling again starts over.</summary>
    public void Toggle()
    {
        int next = ActiveIndex + 1;
        if (next >= Entries.Length) next = -1;
        if (next == -1)
            DisableActive();
        else
            SetActive(next);
    }

    /// <summary>Switches to the system at an index.</summary>
    public void SetActive(int index)
    {
        if (index == ActiveIndex || index < 0 || index >= Entries.Length) return;

        if (ActiveIndex >= 0)
        {
            var old = Entries[ActiveIndex].System;
            old.Dispose();
            old.Enabled = false;
        }

        ActiveIndex = index;

        var next = Entries[ActiveIndex].System;
        next.Enabled = true;
        next.Initialize();
    }

    /// <summary>Turns the running system off and leaves the group with none active.</summary>
    public void DisableActive()
    {
        if (ActiveIndex < 0) return;

        var old = Entries[ActiveIndex].System;
        old.Dispose();
        old.Enabled = false;
        ActiveIndex = -1;
    }

    /// <summary>Returns a system's index in the group, or -1.</summary>
    public int IndexOf(System system)
    {
        for (int i = 0; i < Entries.Length; i++)
            if (Entries[i].System == system) return i;

        return -1;
    }

    /// <summary>Runs an action on every system in the group, active or not.</summary>
    public void ForEach(Action<System> action)
    {
        for (int i = 0; i < Entries.Length; i++)
            action(Entries[i].System);
    }
}
