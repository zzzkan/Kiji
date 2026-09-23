using System.Reflection;
using System.Runtime.CompilerServices;
using Kiji.Generation;

namespace Kiji.Assets;

internal static class ImageProcessorIdentity
{
    private static readonly ConditionalWeakTable<Assembly, string> Code = [];

    internal static string? Get(IImageProcessor processor)
    {
        if (processor.CacheIdentity is not { } settings || processor.GetType().Assembly.IsDynamic) { return null; }
        var code = Code.GetValue(processor.GetType().Assembly, static assembly =>
        {
            var dependencies = IncrementalBuildPlanner.CollectCodeDependencies([assembly]);
            return dependencies.Any(static input => input.EndsWith(":unavailable", StringComparison.Ordinal))
                ? string.Empty
                : BuildFingerprint.HashText(string.Join("\n", dependencies));
        });
        if (code.Length == 0) { return null; }
        return BuildFingerprint.HashText(code + ":" + settings);
    }
}
