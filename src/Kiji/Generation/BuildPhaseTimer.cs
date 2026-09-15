using System.Diagnostics;

namespace Kiji.Generation;

/// <summary>
/// Reports how long each stage of <see cref="StaticSite.PublishAsync"/> took, so a
/// measurement harness can attribute a build's time without patching the build path.
/// Nothing listens by default: <see cref="Observer"/> is null and each
/// <see cref="Mark"/> is a null check.
/// </summary>
/// <remarks>
/// Set <see cref="Observer"/> once per process; it is read on the build thread only.
/// </remarks>
internal sealed class BuildPhaseTimer
{
    internal const string Snapshot = "snapshot";

    // Sub-phases of the snapshot, reported alongside it rather than instead of it.
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

    /// <summary>
    /// Receives (phase name, elapsed) as each stage finishes. Null disables timing.
    /// </summary>
    internal static Action<string, TimeSpan>? Observer { get; set; }

    /// <summary>
    /// Attributes the time since the previous mark (or since construction) to
    /// <paramref name="phase"/> and starts the next one.
    /// </summary>
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
