namespace Kiji.Generation;

/// <summary>
/// Persisted record of the previous build (<c>.kiji/cache/manifest.json</c>):
/// the global fingerprint it was produced under, every page with its inputs and
/// outputs, synced static files, and generated artifacts. The next build skips any
/// page whose fingerprints all still match.
/// </summary>
internal sealed record BuildManifest
{
    internal const int CurrentSchemaVersion = 6;

    public int SchemaVersion { get; init; }

    public string OptionsHash { get; init; } = string.Empty;

    public IReadOnlyList<string> CodeDependencies { get; init; } = [];

    public string HtmlFile { get; init; } = string.Empty;

    // Only scopes the output stamps; it never invalidates the portable HTML cache.
    public string OutputDirectoryHash { get; init; } = string.Empty;

    public IReadOnlyList<BuildManifestPage> Pages { get; init; } = [];

    public IReadOnlyList<string> StaticFiles { get; init; } = [];

    public IReadOnlyList<string> Artifacts { get; init; } = [];

    internal bool IsValid(bool requireHtmlFile = true)
    {
        if (SchemaVersion != CurrentSchemaVersion || string.IsNullOrEmpty(OptionsHash)
            || CodeDependencies is null || Pages is null || StaticFiles is null || Artifacts is null
            || CodeDependencies.Any(string.IsNullOrEmpty)
            || (requireHtmlFile && (HtmlFile is null || !HtmlFile.StartsWith("html-", StringComparison.Ordinal)
                || !HtmlFile.EndsWith(".bin", StringComparison.Ordinal)
                || !IsRelativeOutput(HtmlFile) || Path.GetFileName(HtmlFile) != HtmlFile)))
        {
            return false;
        }

        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in Pages)
        {
            if (page is null || !IsRelativeOutput(page.OutputRelativePath) || !outputs.Add(page.OutputRelativePath)
                || string.IsNullOrEmpty(page.RoutePath) || page.ParametersHash is ""
                || !ArtifactCache.IsHash(page.OutputHash) || page.Dependencies is null || page.AdditionalOutputs is null
                || page.ImageRequests is null || page.ImageRequests.Any(static image => image is null
                    || !IsRelativeOutput(image.Source) || (image.OutputDirectory != "." && !IsRelativeOutput(image.OutputDirectory)))
                || page.Images is null || page.Images.Any(static key => !ArtifactCache.IsHash(key))
                || page.AdditionalOutputs.Any(static output => output is null || !IsRelativeOutput(output.RelativePath) || !ArtifactCache.IsHash(output.Hash))
                || page.Dependencies.Any(static dependency => dependency is null || dependency.Key is null
                    || string.IsNullOrEmpty(dependency.Kind) || string.IsNullOrEmpty(dependency.Fingerprint)))
            {
                return false;
            }
        }

        if (!StaticFiles.All(path => IsRelativeOutput(path) && outputs.Add(path))
            || !Artifacts.All(path => IsRelativeOutput(path) && outputs.Add(path)))
        {
            return false;
        }
        var images = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in Pages.SelectMany(page => page.AdditionalOutputs))
        {
            if (outputs.Contains(image.RelativePath)
                || (images.TryGetValue(image.RelativePath, out var hash) && hash != image.Hash))
            {
                return false;
            }
            images[image.RelativePath] = image.Hash;
        }
        return true;
    }

    internal static bool IsRelativeOutput(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)
            || path.Contains(':', StringComparison.Ordinal) || path.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }
        foreach (var range in path.AsSpan().SplitAny('/', '\\'))
        {
            var segment = path.AsSpan()[range];
            if (segment.IsEmpty || segment is "." or "..") { return false; }
        }
        return true;
    }
}
