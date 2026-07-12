namespace Kiji.Hosting;

internal sealed record WatchedChange(WatchedPathSource Source, WatcherChangeTypes ChangeType, string Path)
{
    internal string ToStatusMessage()
    {
        return $"File {ChangeType.ToDisplayString()}: {ToDisplayPath()}";
    }

    private string ToDisplayPath()
    {
        if (string.IsNullOrWhiteSpace(Path) || System.IO.Path.IsPathRooted(Path))
        {
            return Path;
        }

        var relativePrefix = $".{System.IO.Path.DirectorySeparatorChar}";

        return Path.StartsWith(relativePrefix, StringComparison.Ordinal)
            ? Path
            : relativePrefix + Path;
    }
}
