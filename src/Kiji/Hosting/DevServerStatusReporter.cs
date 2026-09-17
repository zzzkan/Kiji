namespace Kiji.Hosting;

internal sealed class DevServerStatusReporter
{
    private const string KijiPrefix = "kiji";
    private const int DotNetWatchPrefixWidth = 12;
    // Colors match dotnet watch's ConsoleReporter (DarkGray prefix+emoji, default
    // message text, yellow warnings) so interleaved output reads as one stream.
    private const string Yellow = "\u001b[33m";
    private const string EmphasizedUrl = "\u001b[1;4:4;36m";
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
        var useEmoji = !EnvironmentValue.IsTruthy(Environment.GetEnvironmentVariable("DOTNET_WATCH_SUPPRESS_EMOJIS"));
        var useAnsiColor = !Console.IsOutputRedirected &&
            !EnvironmentValue.IsTruthy(Environment.GetEnvironmentVariable("NO_COLOR")) &&
            !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

        return new DevServerStatusReporter(Console.Out, KijiPrefix, useEmoji, useAnsiColor);
    }

    internal void DevServerStarted(
        Uri address,
        string contentPath,
        string? staticPath,
        IReadOnlyList<string> buildInputPaths)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);
        ArgumentNullException.ThrowIfNull(buildInputPaths);

        var displayAddress = _useAnsiColor ? $"{EmphasizedUrl}{address}{Reset}" : address.ToString();
        WriteLine(StatusKind.Started, $"Dev server started at {displayAddress}");
        WriteLine(StatusKind.Change, $"Watching content: '{contentPath}'.");

        if (!string.IsNullOrWhiteSpace(staticPath))
        {
            WriteLine(StatusKind.Change, $"Watching static assets: '{staticPath}'.");
        }

        foreach (var buildInputPath in buildInputPaths)
        {
            WriteLine(StatusKind.Change, $"Watching build input: '{buildInputPath}'.");
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
            reloadedClientCount > 0 ? StatusKind.Reload : StatusKind.Skip,
            reloadedClientCount > 0
                ? $"Reloaded {reloadedClientCount} browser client(s)."
                : "Browser reload skipped: no clients connected; changes will be visible on the next request.");
    }

    internal void CodeUpdated(int reloadedClientCount)
    {
        WriteLine(
            reloadedClientCount > 0 ? StatusKind.Reload : StatusKind.Skip,
            reloadedClientCount > 0
                ? $"Page cache refreshed after a code update; reloaded {reloadedClientCount} browser client(s)."
                : "Page cache refreshed after a code update; browser reload skipped: no clients connected.");
    }

    internal void WatcherError(WatchedPathSource source, string path, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(exception);

        var sourceName = source switch
        {
            WatchedPathSource.Content => "content",
            WatchedPathSource.Static => "static",
            WatchedPathSource.BuildInput => "build input",
            _ => throw new InvalidOperationException($"Unknown watched path source '{source}'."),
        };
        WriteLine(
            StatusKind.Warning,
            $"File watcher for {sourceName} path '{path}' failed: {exception.Message}");
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
                StatusKind.Skip => "↪",
                StatusKind.Warning => "⚠",
                _ => throw new InvalidOperationException($"Unknown status kind '{kind}'."),
            };
        }

        return kind switch
        {
            StatusKind.Started => "[Started]",
            StatusKind.Change => "[Watch]",
            StatusKind.Reload => "[Reload]",
            StatusKind.Skip => "[Skip]",
            StatusKind.Warning => "[Warn]",
            _ => throw new InvalidOperationException($"Unknown status kind '{kind}'."),
        };
    }

    private enum StatusKind
    {
        Started,
        Change,
        Reload,
        Skip,
        Warning,
    }
}
