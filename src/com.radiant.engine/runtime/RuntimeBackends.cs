using System;

namespace com.radiant.engine.runtime;

/// <summary>Chooses the platform integration without coupling the core assembly to one implementation.</summary>
public static class RuntimeBackends
{
    /// <summary>Optional application-wide default. Explicit GameClient backends take precedence.</summary>
    public static Func<IRuntimeBackend> DefaultFactory { get; set; }

    internal static IRuntimeBackend CreateDefault()
    {
        if (DefaultFactory != null) return DefaultFactory() ?? throw new InvalidOperationException("The runtime factory returned null.");
        // The desktop distribution supplies this entry point. Core-only applications
        // choose an explicit backend or register their own factory.
        var name = AppContext.GetData("Radiant.RuntimeBackend") as string
            ?? "com.radiant.engine.runtime.DesktopBackend, radiant";
        var type = Type.GetType(name, throwOnError: false);
        if (type == null || !typeof(IRuntimeBackend).IsAssignableFrom(type))
            throw new InvalidOperationException("No Radiant runtime backend is installed. Pass an IRuntimeBackend or set RuntimeBackends.DefaultFactory.");
        return (IRuntimeBackend)Activator.CreateInstance(type);
    }
}
