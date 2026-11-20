using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Kiji.Hosting;

internal sealed class LiveReloadHub
{
    private static readonly byte[] ReloadMessage = Encoding.UTF8.GetBytes("reload");

    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly Lock _sendLock = new();
    private Task _pendingSend = Task.CompletedTask;

    private byte[]? _diagnosticsMessage;

    internal async Task HandleClientAsync(Func<Task<WebSocket>> acceptWebSocket, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        using var socket = await QueueSendAsync(async () =>
        {
            var accepted = await acceptWebSocket();
            _clients.TryAdd(id, accepted);
            if (_diagnosticsMessage is { } diagnostics)
            {
                await SendAsync(id, accepted, diagnostics, cancellationToken);
            }
            return accepted;
        });

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
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }

    internal Task<int> BroadcastReloadAsync(CancellationToken cancellationToken)
    {
        return QueueSendAsync(() => SendToClientsAsync(ReloadMessage, cancellationToken));
    }

    internal Task<int> ReportDiagnosticsAsync(string[] diagnostics, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.SerializeToUtf8Bytes(diagnostics);
        return QueueSendAsync(() =>
        {
            if (diagnostics.Length == 0 && _diagnosticsMessage is null)
            {
                return Task.FromResult(0);
            }

            _diagnosticsMessage = diagnostics.Length == 0 ? null : message;
            return SendToClientsAsync(message, cancellationToken);
        });
    }

    private Task<T> QueueSendAsync<T>(Func<Task<T>> send)
    {
        lock (_sendLock)
        {
            var next = SendAfterAsync(_pendingSend, send);
            _pendingSend = next;
            return next;
        }
    }

    private static async Task<T> SendAfterAsync<T>(Task previous, Func<Task<T>> send)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return await send();
    }

    private async Task<int> SendToClientsAsync(byte[] message, CancellationToken cancellationToken)
    {
        var sentClients = 0;

        foreach (var (id, socket) in _clients)
        {
            if (await SendAsync(id, socket, message, cancellationToken))
            {
                sentClients++;
            }
        }

        return sentClients;
    }

    private async Task<bool> SendAsync(Guid id, WebSocket socket, byte[] message, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            _clients.TryRemove(id, out _);
            return false;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await socket.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, timeout.Token);
            return true;
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

        return false;
    }
}
