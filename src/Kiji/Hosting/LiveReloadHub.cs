using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace Kiji.Hosting;

/// <summary>
/// Tracks connected live-reload WebSocket clients and broadcasts reload messages.
/// </summary>
internal sealed class LiveReloadHub
{
    private static readonly byte[] ReloadMessage = Encoding.UTF8.GetBytes("reload");

    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();

    internal async Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        _clients.TryAdd(id, socket);

        try
        {
            var buffer = new byte[256];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Server shutting down.
        }
        catch (WebSocketException)
        {
            // Client disconnected abruptly.
        }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }

    internal async Task<int> BroadcastReloadAsync(CancellationToken cancellationToken)
    {
        var reloadedClients = 0;

        foreach (var (id, socket) in _clients)
        {
            if (socket.State != WebSocketState.Open)
            {
                _clients.TryRemove(id, out _);
                continue;
            }

            try
            {
                await socket.SendAsync(ReloadMessage, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
                reloadedClients++;
            }
            catch (WebSocketException)
            {
                _clients.TryRemove(id, out _);
            }
        }

        return reloadedClients;
    }
}
