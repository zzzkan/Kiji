namespace Kiji.Hosting;

internal static class WatcherChangeTypesExtensions
{
    internal static string ToDisplayString(this WatcherChangeTypes changeType)
    {
        return changeType switch
        {
            WatcherChangeTypes.Created => "created",
            WatcherChangeTypes.Changed => "updated",
            WatcherChangeTypes.Deleted => "deleted",
            WatcherChangeTypes.Renamed => "renamed",
            _ => changeType.ToString(),
        };
    }
}
