namespace Kiji.Hosting;

internal sealed class DevServerStatusReporter
{
    private const string KijiDevPrefix = "kiji dev";
    private const int DotNetWatchPrefixWidth = 12;
    // Colors match dotnet watch's ConsoleReporter (DarkGray prefix+emoji, default
    // message text, yellow warnings) so interleaved output reads as one stream.
    private const string Yellow = "\u001b[33m";
    private const string Reset = "\u001b[0m";
    private const string DarkGray = "\u001b[90m";

    private readonly TextWriter _output;
    private readonly string _alignedPrefix;
    private readonly bool _useEmoji;
    private readonly bool _useAnsiColor;
    private readonly Lock _writeLock = new();

    internal DevServerStatusReporter(TextWriter output, string prefix, bool useEmoji, bool useAnsiColor = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        _output = output;
        _alignedPrefix = prefix.PadRight(DotNetWatchPrefixWidth);
        _useEmoji = useEmoji;
        _useAnsiColor = useAnsiColor;
    }

    internal static DevServerStatusReporter CreateForCurrentProcess()
    {
        var useEmoji = !IsTruthy(Environment.GetEnvironmentVariable("DOTNET_WATCH_SUPPRESS_EMOJIS"));
        var useAnsiColor = !Console.IsOutputRedirected &&
            !IsTruthy(Environment.GetEnvironmentVariable("NO_COLOR")) &&
            !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

        return new DevServerStatusReporter(Console.Out, KijiDevPrefix, useEmoji, useAnsiColor);
    }

    internal void DevServerStarted(Uri address, string contentPath, string? staticPath)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);

        WriteLine(StatusKind.Started, $"Started Kiji dev server at {address}");
        WriteLine(StatusKind.Change, $"Watching content files under '{contentPath}'.");

        if (!string.IsNullOrWhiteSpace(staticPath))
        {
            WriteLine(StatusKind.Change, $"Watching static files under '{staticPath}'.");
        }
    }

    internal void ChangesDetected(IReadOnlyList<WatchedChange> changes, int reloadedClientCount)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return;
        }

        foreach (var change in changes)
        {
            WriteLine(StatusKind.Change, change.ToStatusMessage());
        }

        WriteLine(
            reloadedClientCount > 0 ? StatusKind.Reload : StatusKind.Change,
            reloadedClientCount > 0
                ? $"Reloaded {reloadedClientCount} browser client(s)."
                : "Detected changes, but no browser clients were connected.");
    }

    internal void CodeUpdated(int reloadedClientCount)
    {
        // Follows dotnet watch's own "changes applied" line, so stay quiet unless
        // there is actually a browser to refresh.
        if (reloadedClientCount > 0)
        {
            WriteLine(StatusKind.Reload, $"Reloaded {reloadedClientCount} browser client(s).");
        }
    }

    internal void WatcherError(WatchedPathSource source, string path, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(exception);

        WriteLine(
            StatusKind.Warning,
            $"File watcher for {source.ToDisplayString()} path '{path}' failed: {exception.Message}");
    }

    private void WriteLine(StatusKind kind, string message)
    {
        lock (_writeLock)
        {
            if (_useAnsiColor)
            {
                var body = kind is StatusKind.Warning ? $"{Yellow}{message}{Reset}" : message;
                _output.WriteLine($"{DarkGray}{_alignedPrefix} {GetMarker(kind)}{Reset} {body}");
            }
            else
            {
                _output.WriteLine($"{_alignedPrefix} {GetMarker(kind)} {message}");
            }

            _output.Flush();
        }
    }

    private string GetMarker(StatusKind kind)
    {
        if (_useEmoji)
        {
            return kind switch
            {
                StatusKind.Started => "🚀",
                StatusKind.Change => "⌚",
                StatusKind.Reload => "🔥",
                StatusKind.Warning => "⚠",
                _ => throw new InvalidOperationException($"Unknown status kind '{kind}'."),
            };
        }

        return kind switch
        {
            StatusKind.Started => "[Started]",
            StatusKind.Change => "[Watch]",
            StatusKind.Reload => "[Reload]",
            StatusKind.Warning => "[Warn]",
            _ => throw new InvalidOperationException($"Unknown status kind '{kind}'."),
        };
    }

    private static bool IsTruthy(string? value)
    {
        return value is not null &&
            (string.Equals(value, "1", StringComparison.Ordinal) ||
             string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
    }

    private enum StatusKind
    {
        Started,
        Change,
        Reload,
        Warning,
    }
}
