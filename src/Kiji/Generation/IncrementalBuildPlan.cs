using Kiji.Rendering;

namespace Kiji.Generation;

/// <summary>
/// The result of comparing the current site against the previous build manifest:
/// which pages must render and which previous entries carry forward unchanged.
/// </summary>
/// <remarks>
/// There is no "clean everything" outcome. The output directory is reconciled against
/// what this build produced once the build is done, so an unrecognized file is
/// something to delete rather than a reason to redo the work.
/// </remarks>
internal sealed record IncrementalBuildPlan(
    IReadOnlyList<PageRenderRequest> PagesToRender,
    IReadOnlyList<BuildManifestPage> CarriedPages,
    BuildManifest? OldManifest,
    string OptionsHash,
    IReadOnlyList<string> CodeDependencies);
