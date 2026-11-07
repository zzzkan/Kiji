using System.Runtime;

namespace Kiji.Hosting;

/// <summary>
/// Starts multicore JIT for a publish. A publish is one short process whose time goes mostly
/// to compiling code; replaying the methods the previous publish compiled on background
/// threads lets page work find them ready. The profile never affects output.
/// </summary>
internal static class PublishJitProfile
{
    /// <summary>A machine-local directory set by Kiji's MSBuild targets for publish only.</summary>
    internal const string DirectoryProperty = "Kiji.JitProfileDirectory";

    internal static void Start(Func<string, object?> runtimeData)
    {
        if (runtimeData(DirectoryProperty) is not string directory || !Path.IsPathFullyQualified(directory))
        {
            return;
        }

        // The runtime records and replays the profile, and ignores one it cannot use.
        ProfileOptimization.SetProfileRoot(directory);
        ProfileOptimization.StartProfile("publish.jitprofile");
    }
}
