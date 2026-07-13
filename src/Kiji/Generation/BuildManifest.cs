namespace Kiji.Generation;

/// <summary>
/// Persisted record of the previous build (<c>.kiji/cache/build-manifest.json</c>):
/// the global fingerprint it was produced under, every page with its inputs and
/// outputs, synced static files, and generated artifacts. The next build skips any
/// page whose fingerprints all still match.
/// </summary>
internal sealed record BuildManifest
{
    internal const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; }

    public string OptionsHash { get; init; } = string.Empty;

    public IReadOnlyList<string> AssemblyMvids { get; init; } = [];

    public IReadOnlyList<BuildManifestPage> Pages { get; init; } = [];

    public IReadOnlyList<BuildManifestStaticFile> StaticFiles { get; init; } = [];

    public IReadOnlyList<string> Artifacts { get; init; } = [];
}
