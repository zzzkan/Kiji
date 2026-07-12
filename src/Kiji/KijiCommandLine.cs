namespace Kiji;

/// <summary>
/// Parses the Kiji command line: <c>build</c> (default), <c>dev</c>, and <c>preview</c>.
/// </summary>
internal static class KijiCommandLine
{
    internal const int DefaultPort = 8080;

    internal const string Usage = "Usage: [build] | dev [--port <n>] | preview [--port <n>]";

    internal static KijiCommand Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "build";

        return command switch
        {
            "build" => new KijiCommand(KijiCommandKind.Build),
            "dev" => new KijiCommand(KijiCommandKind.Dev, Port: ParsePort(args)),
            "preview" => new KijiCommand(KijiCommandKind.Preview, Port: ParsePort(args)),
            _ => new KijiCommand(KijiCommandKind.Unknown, RawCommand: command),
        };
    }

    private static int ParsePort(string[] args)
    {
        var value = GetOptionValue(args, "--port");
        if (value is null)
        {
            return DefaultPort;
        }

        return int.TryParse(value, out var port) && port is >= 0 and <= 65535
            ? port
            : throw new ArgumentException($"Invalid port: '{value}'.");
    }

    private static string? GetOptionValue(string[] args, string name)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
