namespace com.radiant.engine.runtime;

public static class RuntimeInfo
{
    public static string BuildTag { get; } = $"Radiant {typeof(RuntimeInfo).Assembly.GetName().Version}";
}
