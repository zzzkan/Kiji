using Kiji.Rendering;

namespace Kiji.Generation;

/// <summary>
/// The result of comparing the current site against the previous build manifest:
/// which pages must render and which previous entries carry forward unchanged.
/// </summary>
internal sealed record IncrementalBuildPlan(
    IReadOnlyList<PageRenderRequest> PagesToRender,
    IReadOnlyList<BuildManifestPage> CarriedPages,
    BuildManifest? OldManifest,
    string OptionsHash,
    IReadOnlyList<string> CodeDependencies);
