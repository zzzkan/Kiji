using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kiji.Hosting;

internal static class WatchDiagnosticsRelay
{
    internal const string EndpointKey = "ASPNETCORE_AUTO_RELOAD_WS_ENDPOINT";
    internal const string ServerKeyKey = "ASPNETCORE_AUTO_RELOAD_WS_KEY";

    internal static async Task RunAsync(
        string? endpoints,
        string? serverKey,
        Func<string[], Task> report,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(endpoints) || string.IsNullOrEmpty(serverKey))
        {
            return;
        }

        try
        {
            using var socket = new ClientWebSocket();
            using (var rsa = RSA.Create())
            {
                rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(serverKey), out _);
                var secret = rsa.Encrypt(RandomNumberGenerator.GetBytes(32), RSAEncryptionPadding.OaepSHA256);
                socket.Options.AddSubProtocol(Uri.EscapeDataString(Convert.ToBase64String(secret)));
            }
            socket.Options.SetRequestHeader("Origin", "http://localhost");
            await socket.ConnectAsync(new Uri(endpoints.Split(',')[0]), cancellationToken);

            var buffer = new byte[16 * 1024];
            using var message = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var diagnostics = ReadDiagnostics(message.GetBuffer().AsMemory(0, (int)message.Length));
                message.SetLength(0);
                if (diagnostics is not null)
                {
                    await report(diagnostics);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is WebSocketException or JsonException
            or FormatException or CryptographicException or InvalidOperationException or KeyNotFoundException)
        {
        }
    }

    private static string[]? ReadDiagnostics(ReadOnlyMemory<byte> message)
    {
        using var json = JsonDocument.Parse(message);
        var root = json.RootElement;
        if (!root.GetProperty("type").ValueEquals("ReportDiagnostics"))
        {
            return null;
        }

        return [.. root.GetProperty("diagnostics").EnumerateArray().Select(static diagnostic => diagnostic.GetString() ?? "")];
    }
}
