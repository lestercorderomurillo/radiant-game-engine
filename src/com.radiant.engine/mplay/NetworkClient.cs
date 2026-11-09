using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using com.radiant.engine.runtime;

namespace com.radiant.engine.mplay;

/// <summary>Owns a TCP connection and routes received messages to its game server.</summary>
public class NetworkClient
{
    private readonly TcpClient TcpClient;

    private readonly GameServer GameServer;

    private NetworkStream Stream;

    private bool IsListening;

    public string PlayerId { get; set; }

    /// <summary>Binds an accepted TCP connection to its owning game server.</summary>
    public NetworkClient(TcpClient tcpClient, GameServer server)
    {
        TcpClient = tcpClient;
        GameServer = server;
        Stream = tcpClient.GetStream();
        PlayerId = string.Empty;
    }

    /// <summary>Starts receiving messages from the connected client.</summary>
    public void Listen()
    {
        IsListening = true;
        Task.Run(() => ProcessMessagesAsync());
    }

    /// <summary>Closes the client's connection and cancels message processing.</summary>
    public void Disconnect()
    {
        IsListening = false;

        try
        {
            Stream?.Close();
            TcpClient?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error disconnecting client: {ex.Message}");
        }
    }

    /// <summary>Reads incoming TCP data and dispatches decoded messages to the server.</summary>
    private async Task ProcessMessagesAsync()
    {
        byte[] buffer = new byte[4096];

        try
        {
            while (IsListening && TcpClient.Connected)
            {
                int bytesRead = await Stream.ReadAsync(buffer, 0, buffer.Length);

                if (bytesRead == 0)
                {
                    // Client disconnected
                    break;
                }

                string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var message = JsonSerializer.Deserialize<NetworkMessage>(json);

                if (message != null) GameServer.HandleMessage(this, message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing client messages: {ex.Message}");
        }
        finally
        {
            GameServer.RemoveClient(this);
            Disconnect();
        }
    }

    /// <summary>Serializes and sends a message over the client connection.</summary>
    public void SendMessage(NetworkMessage message)
    {
        if (!TcpClient.Connected) return;

        string json = JsonSerializer.Serialize(message);
        byte[] data = Encoding.UTF8.GetBytes(json);

        Stream.Write(data, 0, data.Length);
    }
}
