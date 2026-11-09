namespace com.radiant.engine.runtime;

/// <summary>Default desktop backend. MonoGame remains behind the runtime host boundary.</summary>
public sealed class MonoGameBackend : IRuntimeBackend
{
    public IRuntimeHost CreateHost(GameLoop Loop, WindowOptions Options) => new Window(Loop, Options);
}
