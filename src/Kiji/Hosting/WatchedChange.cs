namespace Kiji.Hosting;

internal sealed record WatchedChange(
    WatchedPathSource Source,
    WatcherChangeTypes ChangeType,
    string FullPath,
    string DisplayPath,
    string? OldDisplayPath = null)
{
    internal string ToStatusMessage()
    {
        var subject = Source switch
        {
            WatchedPathSource.Content => "Content",
            WatchedPathSource.Static => "Static asset",
            WatchedPathSource.BuildInput => "Build input",
            _ => throw new InvalidOperationException($"Unknown watched path source '{Source}'."),
        };
        var action = ChangeType switch
        {
            WatcherChangeTypes.Created => "created",
            WatcherChangeTypes.Changed => "changed",
            WatcherChangeTypes.Deleted => "deleted",
            WatcherChangeTypes.Renamed => "renamed",
            _ => ChangeType.ToString(),
        };

        return ChangeType is WatcherChangeTypes.Renamed && !string.IsNullOrWhiteSpace(OldDisplayPath)
            ? $"{subject} {action}: {OldDisplayPath} -> {DisplayPath}"
            : $"{subject} {action}: {DisplayPath}";
    }
}
