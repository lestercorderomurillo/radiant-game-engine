using System;

namespace com.radiant.engine.core;

/// <summary>Switches among mutually exclusive systems, initializing and disposing them as needed.</summary>
public class SystemGroup
{
    private readonly (string Name, System System)[] Entries;
    private int ActiveIndex;

    public string Name { get; }
    public string ActiveName => ActiveIndex >= 0 ? Entries[ActiveIndex].Name : null;
    public System Active => ActiveIndex >= 0 ? Entries[ActiveIndex].System : null;
    public int ActiveIdx => ActiveIndex;
    public string[] Names => Array.ConvertAll(Entries, e => e.Name);

    /// <summary>Creates a named selection group and locates its initially enabled entry.</summary>
    public SystemGroup(string name, params (string name, System system)[] entries)
    {
        Name = name;
        Entries = entries;
        ActiveIndex = Array.FindIndex(entries, e => e.system.Enabled);
        if (ActiveIndex < 0) ActiveIndex = 0;
    }

    /// <summary>Activates the next system, cycling through a disabled state after the last entry.</summary>
    public void Toggle()
    {
        int next = ActiveIndex + 1;
        if (next >= Entries.Length) next = -1;
        if (next == -1)
            DisableActive();
        else
            SetActive(next);
    }

    /// <summary>Disposes the current system and initializes the selected entry.</summary>
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

    /// <summary>Disposes and disables the active system, leaving the group inactive.</summary>
    public void DisableActive()
    {
        if (ActiveIndex < 0) return;

        var old = Entries[ActiveIndex].System;
        old.Dispose();
        old.Enabled = false;
        ActiveIndex = -1;
    }

    /// <summary>Returns the entry index for a system, or -1 when it is not in the group.</summary>
    public int IndexOf(System system)
    {
        for (int i = 0; i < Entries.Length; i++)
            if (Entries[i].System == system) return i;

        return -1;
    }

    /// <summary>Invokes an action for every system in the group.</summary>
    public void ForEach(Action<System> action)
    {
        for (int i = 0; i < Entries.Length; i++)
            action(Entries[i].System);
    }
}
