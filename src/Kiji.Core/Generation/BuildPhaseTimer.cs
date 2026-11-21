using System.Diagnostics;

namespace Kiji.Generation;

internal sealed class BuildPhaseTimer
{
    internal const string Snapshot = "snapshot";

    internal const string Discovery = "snapshot.discovery";
    internal const string Routes = "snapshot.routes";
    internal const string RouteEntries = "snapshot.routes.entries";
    internal const string RouteValidation = "snapshot.routes.validate";
    internal const string Planning = "snapshot.planning";

    internal const string Plan = "plan";
    internal const string Clean = "clean";
    internal const string Static = "static";
    internal const string Render = "render";
    internal const string Artifacts = "artifacts";
    internal const string Entries = "entries";
    internal const string Reconcile = "reconcile";
    internal const string Manifest = "manifest";

    private long _timestamp = Stopwatch.GetTimestamp();

    internal static Action<string, TimeSpan>? Observer { get; set; }

    internal void Mark(string phase)
    {
        var observer = Observer;
        if (observer is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        observer(phase, Stopwatch.GetElapsedTime(_timestamp, now));
        _timestamp = now;
    }
}
