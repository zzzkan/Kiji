namespace Kiji;

/// <summary>
/// The command selected from the command line, with its options.
/// </summary>
internal enum KijiCommandKind
{
    Build,
    Clean,
    Serve,
    Preview,
    Unknown,
}

/// <summary>
/// A parsed Kiji command line. Only the members relevant to <see cref="Kind"/> are meaningful.
/// </summary>
internal sealed record KijiCommand(
    KijiCommandKind Kind,
    string? Output = null,
    bool Clean = true,
    int Port = KijiCommandLine.DefaultPort,
    string? RawCommand = null);

/// <summary>
/// Parses the Kiji command line: <c>build</c> (default), <c>clean</c>, <c>serve</c>, and <c>preview</c>.
/// </summary>
internal static class KijiCommandLine
{
    internal const int DefaultPort = 8080;

    internal const string Usage = "Usage: [build [--output <path>] [--no-clean]] | clean | serve [--port <n>] | preview [--port <n>]";

    internal static KijiCommand Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "build";

        return command switch
        {
            "build" => new KijiCommand(
                KijiCommandKind.Build,
                Output: GetOptionValue(args, "--output"),
                Clean: !HasFlag(args, "--no-clean")),
            "clean" => new KijiCommand(KijiCommandKind.Clean),
            "serve" => new KijiCommand(KijiCommandKind.Serve, Port: ParsePort(args)),
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

    private static bool HasFlag(string[] args, string name)
    {
        for (var i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
