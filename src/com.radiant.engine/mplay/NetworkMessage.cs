namespace com.radiant.engine.mplay;

/// <summary>Typed JSON payload exchanged between game clients and servers.</summary>
public class NetworkMessage
{
    public string Type { get; set; }

    public string Data { get; set; }
}
