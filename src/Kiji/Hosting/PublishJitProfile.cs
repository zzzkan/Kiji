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
/// one while the publish finishes. <see cref="StaticSite.CreateBuilder(string[])"/> starts the
/// profile before the host exists and registers it as a service; the publish ends it.
/// </remarks>
internal sealed class PublishJitProfile
{
    internal const string FileName = "publish.jitprofile";

    private const string RecordingName = "recording.jitprofile";

    private const string SessionName = "jit-session";

    private static int s_started;

    private readonly string _directory;
    private readonly string _sessionDirectory;
    private PublishJitProfileStamp? _stamp;
    private bool _keep;
    private Task? _written;
    private bool _saved;

    private PublishJitProfile(string directory, string sessionDirectory)
    {
        _directory = directory;
        _sessionDirectory = sessionDirectory;
    }

    internal static string PortablePath(string kijiDirectory) => Path.Combine(kijiDirectory, "jit", FileName);

    /// <summary>Starts the profile of this process's publish.</summary>
    /// <returns>The started profile, or <see langword="null"/> when this process records none.</returns>
    internal static PublishJitProfile? Start(SiteRuntimeMetadata runtimeMetadata)
    {
        if (runtimeMetadata.JitProfileDirectory is not string directory || !Path.IsPathFullyQualified(directory)
            || Interlocked.Exchange(ref s_started, 1) != 0)
        {
            return null;
        }

        var kept = Path.Combine(directory, FileName);
        if (runtimeMetadata.ProjectDirectory is string project && Path.IsPathFullyQualified(project)
            && !File.Exists(kept))
        {
            var portable = PortablePath(Path.Combine(project, ".kiji"));
            if (TryCopy(portable, kept)) { TryCopy(portable + ".stamp", kept + ".stamp"); }
        }

        var sessionDirectory = Path.Combine(directory, SessionName);
        var recording = Path.Combine(sessionDirectory, RecordingName);
        try
        {
            Directory.CreateDirectory(sessionDirectory);
            if (!File.Exists(kept) && File.Exists(recording)) { File.Move(recording, kept); }
            if (File.Exists(kept)) { File.Move(kept, recording, overwrite: true); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        ProfileOptimization.SetProfileRoot(sessionDirectory);
        ProfileOptimization.StartProfile(RecordingName);
        try
        {
            if (File.Exists(recording)) { File.Move(recording, kept, overwrite: true); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return new PublishJitProfile(directory, sessionDirectory);
    }

    /// <summary>
    /// Ends the recording. Call it when pages have rendered and again, with
    /// <paramref name="publishCompleted"/>, when the publish has finished; follow with
    /// <see cref="SaveAsync"/> once it has succeeded. A no-op when this process started no
    /// has already ended it.
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
    internal void Stop(string codeIdentity, int renderedPages, bool publishCompleted)
    {
        if (_written is not null)
        {
            return;
        }

        var kept = Path.Combine(_directory, FileName);
        var recorded = File.Exists(kept) ? PublishJitProfileStamp.Read(kept + ".stamp") : null;
        var sameCode = recorded?.CodeIdentity == codeIdentity;
        if (recorded is null || (renderedPages > 0 && !sameCode))
        {
            _keep = true;
        }
        else if (renderedPages == 0 || (recorded.Complete && recorded.RenderedPages >= renderedPages))
        {
            TryDeleteDirectory(_sessionDirectory);
        }
        else if (!publishCompleted)
        {
            return;
        }
        else
        {
            _keep = recorded.Complete || renderedPages >= recorded.RenderedPages;
            if (!_keep) { TryDeleteDirectory(_sessionDirectory); }
        }

        _stamp = new PublishJitProfileStamp(codeIdentity, renderedPages, publishCompleted);
        _written = Task.Run(static () => ProfileOptimization.StartProfile(null));
    }

    /// <summary>
    /// Keeps the recording <see cref="Stop"/> ended. Call it under the cache lease after a
    /// successful publish. A no-op until <see cref="Stop"/> has ended the recording.
    /// </summary>
    /// <param name="kijiDirectory">The site's <c>.kiji</c> directory.</param>
    internal async Task SaveAsync(string kijiDirectory)
    {
        if (_written is not { } written || _saved)
        {
            return;
        }

        _saved = true;
        await written;
        try
        {
            var recording = Path.Combine(_sessionDirectory, RecordingName);
            if (_keep && File.Exists(recording))
            {
                var bytes = File.ReadAllBytes(recording);
                var stamp = System.Text.Encoding.UTF8.GetBytes(_stamp!.ToString());
                var kept = Path.Combine(_directory, FileName);
                var portable = PortablePath(kijiDirectory);
                Replace(kept, bytes, stamp);
                Replace(portable, bytes, stamp);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        TryDeleteDirectory(_sessionDirectory);
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
}
