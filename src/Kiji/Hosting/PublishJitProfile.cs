using System.Runtime;
using Kiji.Generation;

namespace Kiji.Hosting;

/// <summary>
/// Starts multicore JIT for a publish. A publish is one short process whose time goes mostly
/// to compiling code; replaying the methods an earlier publish compiled on background
/// threads lets page work find them ready. The profile never affects output.
/// </summary>
/// <remarks>
/// The runtime records into the machine-local staging directory. A successful publish also
/// saves the profile to <c>.kiji/jit</c>, which a clean checkout, such as CI, can restore
/// independently of the page cache in <c>.kiji/cache</c>. The runtime writes a profile one
/// record at a time, thousands of small writes, so a publish keeps the profile it replayed
/// when the same code recorded it over at least as many pages, and otherwise writes the new
/// one while the publish finishes.
/// </remarks>
internal static class PublishJitProfile
{
    internal const string FileName = "publish.jitprofile";

    private const string RecordingName = "recording.jitprofile";

    // The runtime replays and records the same file, and offers no way to drop a recording:
    // it writes whenever the file can be opened. The recording therefore has a directory of
    // its own, which is removed to discard it. A concurrent publish of the same project that
    // loses its recording this way only leaves the kept profile as it was.
    private const string SessionName = "jit-session";

    private static Session? s_session;

    internal static string PortablePath(string kijiDirectory) => Path.Combine(kijiDirectory, "jit", FileName);

    internal static void Start(SiteRuntimeMetadata runtimeMetadata)
    {
        if (runtimeMetadata.JitProfileDirectory is not string directory || !Path.IsPathFullyQualified(directory))
        {
            return;
        }

        var kept = Path.Combine(directory, FileName);
        if (runtimeMetadata.ProjectDirectory is string project && Path.IsPathFullyQualified(project)
            && !File.Exists(kept))
        {
            // A restored portable profile seeds a staging directory that has none.
            var portable = PortablePath(Path.Combine(project, ".kiji"));
            if (TryCopy(portable, kept)) { TryCopy(portable + ".stamp", kept + ".stamp"); }
        }

        var sessionDirectory = Path.Combine(directory, SessionName);
        try
        {
            Directory.CreateDirectory(sessionDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
        TryCopy(kept, Path.Combine(sessionDirectory, RecordingName));

        // The runtime replays the profile, ignores one it cannot use, and records a new one.
        ProfileOptimization.SetProfileRoot(sessionDirectory);
        ProfileOptimization.StartProfile(RecordingName);
        s_session = new Session(directory, sessionDirectory);
    }

    /// <summary>
    /// Ends the recording. Call it when pages have rendered and again, with
    /// <paramref name="publishCompleted"/>, when the publish has finished; follow with
    /// <see cref="SaveAsync"/> once it has succeeded. A no-op when this process started no
    /// profile or has already ended it.
    /// </summary>
    /// <remarks>
    /// A publish with no profile for its code gains nothing from its own recording, so that
    /// one ends with rendering and is written while the publish finishes. The next publish
    /// replays it and records through to the end once, after which the profile is kept.
    /// </remarks>
    /// <param name="codeIdentity">Identifies the code this publish ran; see <see cref="CodeIdentity"/>.</param>
    /// <param name="renderedPages">
    /// Pages this publish rendered. A publish that rendered none recorded no rendering code,
    /// so an existing profile is kept for the next publish that renders.
    /// </param>
    /// <param name="publishCompleted">Whether the recording covers the whole publish.</param>
    internal static void Stop(string codeIdentity, int renderedPages, bool publishCompleted)
    {
        if (Volatile.Read(ref s_session) is not { Written: null } session)
        {
            return;
        }

        var kept = Path.Combine(session.Directory, FileName);
        var recorded = File.Exists(kept) ? PublishJitProfileStamp.Read(kept + ".stamp") : null;
        var sameCode = recorded?.CodeIdentity == codeIdentity;
        if (recorded is null || (renderedPages > 0 && !sameCode))
        {
            // Nothing usable was replayed. Any recording is an improvement.
            session.Keep = true;
        }
        else if (renderedPages == 0 || (recorded.Complete && recorded.RenderedPages >= renderedPages))
        {
            // Without its directory the runtime cannot open the recording, and skips the write.
            TryDeleteDirectory(session.SessionDirectory);
        }
        else if (!publishCompleted)
        {
            // Replaying a profile of this code: record the rest of the publish as well.
            return;
        }
        else
        {
            // Keep the wider rendering coverage of a profile that only lacks the end of a publish.
            session.Keep = recorded.Complete || renderedPages >= recorded.RenderedPages;
            if (!session.Keep) { TryDeleteDirectory(session.SessionDirectory); }
        }

        session.Stamp = new PublishJitProfileStamp(codeIdentity, renderedPages, publishCompleted);
        // Stopping writes the recorded profile now instead of at process exit.
        session.Written = Task.Run(static () => ProfileOptimization.StartProfile(null));
    }

    /// <summary>
    /// Keeps the recording <see cref="Stop"/> ended. Call it under the cache lease after a
    /// successful publish.
    /// </summary>
    /// <param name="kijiDirectory">The site's <c>.kiji</c> directory.</param>
    internal static async Task SaveAsync(string kijiDirectory)
    {
        if (Interlocked.Exchange(ref s_session, null) is not { Written: { } written } session)
        {
            return;
        }

        await written;
        try
        {
            var recording = Path.Combine(session.SessionDirectory, RecordingName);
            if (session.Keep && File.Exists(recording))
            {
                var bytes = File.ReadAllBytes(recording);
                var stamp = System.Text.Encoding.UTF8.GetBytes(session.Stamp!.ToString());
                var kept = Path.Combine(session.Directory, FileName);
                var portable = PortablePath(kijiDirectory);
                // A stamp never describes a profile other than the one beside it: it is
                // removed before its profile changes and written after.
                Replace(kept, bytes, stamp);
                Replace(portable, bytes, stamp);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Only a later publish's startup time depends on it.
        }
        TryDeleteDirectory(session.SessionDirectory);
    }

    /// <summary>Identifies the code a profile was recorded for.</summary>
    internal static string CodeIdentity(IReadOnlyList<string> codeDependencies) =>
        BuildFingerprint.HashText(string.Join('\n', codeDependencies));

    private static void Replace(string path, byte[] profile, byte[] stamp)
    {
        var stampPath = path + ".stamp";
        if (File.Exists(stampPath)) { File.Delete(stampPath); }
        ArtifactCache.WriteAtomic(path, profile);
        ArtifactCache.WriteAtomic(stampPath, stamp);
    }

    private static bool TryCopy(string source, string destination)
    {
        try
        {
            if (File.Exists(source))
            {
                File.Copy(source, destination, overwrite: true);
                return true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return false;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private sealed class Session(string directory, string sessionDirectory)
    {
        public string Directory { get; } = directory;
        public string SessionDirectory { get; } = sessionDirectory;
        public PublishJitProfileStamp? Stamp { get; set; }
        public bool Keep { get; set; }
        public Task? Written { get; set; }
    }
}
