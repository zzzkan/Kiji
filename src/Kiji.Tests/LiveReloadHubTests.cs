using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Kiji.Hosting;
using Xunit;

namespace Kiji.Tests;

public sealed class LiveReloadHubTests
{
    [Fact]
    public async Task BroadcastReloadAsync_FailedAcceptanceDoesNotBlockLaterBroadcasts()
    {
        var hub = new LiveReloadHub();
        var failure = new WebSocketException("Handshake failed.");
        var actual = await Assert.ThrowsAsync<WebSocketException>(() =>
            hub.HandleClientAsync(() => Task.FromException<WebSocket>(failure), CancellationToken.None));

        Assert.Same(failure, actual);
        Assert.Equal(0, await hub.BroadcastReloadAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public async Task BroadcastReloadAsync_WaitsForAcceptingClientToBeRegistered()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        using var server = await listener.AcceptTcpClientAsync(timeout.Token);
        using var clientSocket = WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan);
        using var serverSocket = WebSocket.CreateFromStream(server.GetStream(), true, null, Timeout.InfiniteTimeSpan);
        var hub = new LiveReloadHub();
        var accepted = new TaskCompletionSource<WebSocket>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The client handshake has finished, but the server's accept continuation
        // has not resumed yet. A code update arriving here must not lose the client.
        var handling = hub.HandleClientAsync(() => accepted.Task, timeout.Token);
        var broadcast = hub.BroadcastReloadAsync(timeout.Token);
        var waitedForRegistration = !broadcast.IsCompleted;
        accepted.SetResult(serverSocket);

        try
        {
            Assert.True(waitedForRegistration);
            Assert.Equal(1, await broadcast.WaitAsync(timeout.Token));
            var buffer = new byte[64];
            var message = await clientSocket.ReceiveAsync(buffer, timeout.Token);
            Assert.Equal(WebSocketMessageType.Text, message.MessageType);
            Assert.True(message.EndOfMessage);
            Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, message.Count));
            await clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
            await handling.WaitAsync(timeout.Token);
            Assert.Equal(0, await hub.BroadcastReloadAsync(timeout.Token));
        }
        finally
        {
            await timeout.CancelAsync();
            await handling;
        }
    }
}
