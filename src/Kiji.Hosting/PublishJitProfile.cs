using System.Runtime;
using Kiji.Generation;

namespace Kiji.Hosting;

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
