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
    private readonly Lock _sendLock = new();
    private Task _pendingSend = Task.CompletedTask;

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
                    await QueueSendAsync(async () =>
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                        return 0;
                    });
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

    internal Task<int> BroadcastReloadAsync(CancellationToken cancellationToken)
    {
        // WebSocket supports only one concurrent send. File saves and code updates
        // can arrive together, so serialize broadcasts and close acknowledgements.
        return QueueSendAsync(() => SendReloadAsync(cancellationToken));
    }

    private Task<int> QueueSendAsync(Func<Task<int>> send)
    {
        lock (_sendLock)
        {
            var next = SendAfterAsync(_pendingSend, send);
            _pendingSend = next;
            return next;
        }
    }

    private static async Task<int> SendAfterAsync(Task previous, Func<Task<int>> send)
    {
        // A failed or cancelled broadcast must not poison subsequent notifications.
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return await send();
    }

    private async Task<int> SendReloadAsync(CancellationToken cancellationToken)
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
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await socket.SendAsync(ReloadMessage, WebSocketMessageType.Text, endOfMessage: true, timeout.Token);
                reloadedClients++;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _clients.TryRemove(id, out _);
                socket.Abort();
            }
            catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException)
            {
                _clients.TryRemove(id, out _);
            }
        }

        return reloadedClients;
    }
}
