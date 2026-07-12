namespace Kiji.Hosting;

internal static class WatchedPathSourceExtensions
{
    internal static string ToDisplayString(this WatchedPathSource source)
    {
        return source switch
        {
            WatchedPathSource.Content => "content",
            WatchedPathSource.Static => "static",
            _ => throw new InvalidOperationException($"Unknown watched path source '{source}'."),
        };
    }
}
