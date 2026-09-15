namespace Kiji.Hosting;

internal sealed record WatchedChange(WatchedPathSource Source, WatcherChangeTypes ChangeType, string Path)
{
    internal string ToStatusMessage()
    {
        var action = ChangeType switch
        {
            WatcherChangeTypes.Created => "created",
            WatcherChangeTypes.Changed => "updated",
            WatcherChangeTypes.Deleted => "deleted",
            WatcherChangeTypes.Renamed => "renamed",
            _ => ChangeType.ToString(),
        };
        var relativePrefix = $".{System.IO.Path.DirectorySeparatorChar}";
        var displayPath = string.IsNullOrWhiteSpace(Path)
            || System.IO.Path.IsPathRooted(Path)
            || Path.StartsWith(relativePrefix, StringComparison.Ordinal)
            ? Path
            : relativePrefix + Path;

        return $"File {action}: {displayPath}";
    }
}
