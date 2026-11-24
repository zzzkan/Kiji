using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kiji.Analyzers.Tests;

internal sealed class BuildProperties(IReadOnlyDictionary<string, string> properties) : AnalyzerConfigOptions
{
    public static BuildProperties Empty { get; } = new(new Dictionary<string, string>());

    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        const string prefix = "build_property.";
        if (key.StartsWith(prefix, StringComparison.Ordinal) && properties.TryGetValue(key[prefix.Length..], out var found))
        {
            value = found;
            return true;
        }

        value = null;
        return false;
    }
}
