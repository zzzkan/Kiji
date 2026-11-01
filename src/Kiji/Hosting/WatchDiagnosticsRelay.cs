using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kiji.Hosting;

/// <summary>
/// Receives the compilation errors that dotnet watch reports while they block hot reload.
/// dotnet watch sends them only to clients of its browser refresh server, and the dev
/// server deliberately does not load the middleware that connects browsers there: Kiji
/// alone reloads pages. Kiji connects as a client instead and ignores everything but
/// these reports. The endpoint, handshake and messages are not a public contract, so any
/// failure leaves the errors in the terminal only.
/// </summary>
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
        // Set only for ASP.NET Core projects; Kiji.targets marks Razor SDK sites as one while watching.
        if (string.IsNullOrEmpty(endpoints) || string.IsNullOrEmpty(serverKey))
        {
            return;
        }

        try
        {
            using var socket = new ClientWebSocket();
            // The server accepts clients that prove they received its public key.
            using (var rsa = RSA.Create())
            {
                rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(serverKey), out _);
                var secret = rsa.Encrypt(RandomNumberGenerator.GetBytes(32), RSAEncryptionPadding.OaepSHA256);
                socket.Options.AddSubProtocol(Uri.EscapeDataString(Convert.ToBase64String(secret)));
            }
            // It also accepts only loopback origins, like pages served by the dev server.
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
            // The dev server is stopping.
        }
        catch (Exception exception) when (exception is WebSocketException or JsonException
            or FormatException or CryptographicException or InvalidOperationException or KeyNotFoundException)
        {
            // dotnet watch exited or changed its protocol: errors stay in the terminal.
        }
    }

    // {"type":"ReportDiagnostics","diagnostics":["..."]}; an empty array clears them.
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
