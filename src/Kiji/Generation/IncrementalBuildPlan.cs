using Kiji.Rendering;

namespace Kiji.Generation;

/// <summary>
/// The result of comparing the current site against the previous build manifest:
/// which pages must render, which previous entries carry forward unchanged, and
/// whether the output directory must be fully rebuilt.
/// </summary>
internal sealed record IncrementalBuildPlan(
    bool FullClean,
    IReadOnlyList<PageRenderRequest> PagesToRender,
    IReadOnlyList<BuildManifestPage> CarriedPages,
    BuildManifest? OldManifest,
    string OptionsHash,
    IReadOnlyList<string> AssemblyMvids,
    string ContentSetFingerprint);
