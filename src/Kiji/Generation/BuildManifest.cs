namespace Kiji.Generation;

/// <summary>
/// Persisted record of the previous build (<c>.kiji/cache/build-manifest.json</c>):
/// the global fingerprint it was produced under, every page with its inputs and
/// outputs, synced static files, and generated artifacts. The next build skips any
/// page whose fingerprints all still match.
/// </summary>
internal sealed record BuildManifest
{
    internal const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; }

    public string OptionsHash { get; init; } = string.Empty;

    public IReadOnlyList<string> AssemblyMvids { get; init; } = [];

    public IReadOnlyList<BuildManifestPage> Pages { get; init; } = [];

    public IReadOnlyList<BuildManifestStaticFile> StaticFiles { get; init; } = [];

    public IReadOnlyList<string> Artifacts { get; init; } = [];

    internal bool IsValid()
    {
        if (SchemaVersion != CurrentSchemaVersion || string.IsNullOrEmpty(OptionsHash)
            || AssemblyMvids is null || Pages is null || StaticFiles is null || Artifacts is null
            || AssemblyMvids.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in Pages)
        {
            if (page is null || !IsRelativeOutput(page.OutputRelativePath) || !outputs.Add(page.OutputRelativePath)
                || string.IsNullOrEmpty(page.RoutePath) || page.ParametersHash is ""
                || string.IsNullOrEmpty(page.OutputHash) || page.Dependencies is null || page.AdditionalOutputs is null
                || page.AdditionalOutputs.Any(static path => !IsRelativeOutput(path))
                || page.Dependencies.Any(static dependency => dependency is null || dependency.Key is null
                    || string.IsNullOrEmpty(dependency.Kind) || string.IsNullOrEmpty(dependency.Fingerprint)))
            {
                return false;
            }
        }

        return StaticFiles.All(file => file is not null && IsRelativeOutput(file.RelativePath) && outputs.Add(file.RelativePath))
            && Artifacts.All(path => IsRelativeOutput(path) && outputs.Add(path));
    }

    private static bool IsRelativeOutput(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path)
            && !path.Contains(':', StringComparison.Ordinal) && !path.Contains('\0', StringComparison.Ordinal)
            && path.Split(['/', '\\']).All(static segment => segment.Length > 0 && segment is not ("." or ".."));
    }
}
