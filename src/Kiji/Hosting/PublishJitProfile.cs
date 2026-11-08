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
/// independently of the page cache in <c>.kiji/cache</c>.
/// </remarks>
internal static class PublishJitProfile
{
    /// <summary>A machine-local directory set by Kiji's MSBuild targets for publish only.</summary>
    internal const string DirectoryProperty = "Kiji.JitProfileDirectory";

    internal const string FileName = "publish.jitprofile";

    // The runtime replays and records the same file name, so each publish records into a copy
    // and keeps it only when it covered rendering.
    private const string RecordingName = "recording.jitprofile";

    private static string? s_directory;

    internal static string PortablePath(string kijiDirectory) => Path.Combine(kijiDirectory, "jit", FileName);

    internal static void Start(Func<string, object?> runtimeData)
    {
        if (runtimeData(DirectoryProperty) is not string directory || !Path.IsPathFullyQualified(directory))
        {
            return;
        }

        var kept = Path.Combine(directory, FileName);
        if (runtimeData("Kiji.ProjectDirectory") is string project && Path.IsPathFullyQualified(project)
            && !File.Exists(kept))
        {
            // A restored portable profile seeds a staging directory that has none.
            TryCopy(PortablePath(Path.Combine(project, ".kiji")), kept);
        }
        TryCopy(kept, Path.Combine(directory, RecordingName));

        // The runtime replays the profile, ignores one it cannot use, and records a new one.
        ProfileOptimization.SetProfileRoot(directory);
        ProfileOptimization.StartProfile(RecordingName);
        s_directory = directory;
    }

    /// <summary>
    /// Ends the recording. Call it under the cache lease after a successful publish; it is a
    /// no-op when this process started no profile.
    /// </summary>
    /// <param name="kijiDirectory">The site's <c>.kiji</c> directory.</param>
    /// <param name="renderedPages">
    /// Pages this publish rendered. A publish that rendered none recorded no rendering code,
    /// so an existing profile is kept for the next publish that renders.
    /// </param>
    internal static void Save(string kijiDirectory, int renderedPages)
    {
        if (Interlocked.Exchange(ref s_directory, null) is not { } directory)
        {
            return;
        }

        // Stopping writes the recorded profile now instead of at process exit.
        ProfileOptimization.StartProfile(null);
        if (renderedPages == 0 && File.Exists(Path.Combine(directory, FileName)))
        {
            return;
        }

        try
        {
            var recording = Path.Combine(directory, RecordingName);
            if (File.Exists(recording))
            {
                var bytes = File.ReadAllBytes(recording);
                ArtifactCache.WriteAtomic(Path.Combine(directory, FileName), bytes);
                ArtifactCache.WriteAtomic(PortablePath(kijiDirectory), bytes);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Only a later publish's startup time depends on it.
        }
    }

    private static void TryCopy(string source, string destination)
    {
        try
        {
            if (File.Exists(source))
            {
                File.Copy(source, destination, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
