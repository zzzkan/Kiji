using Kiji.Incremental;
using Kiji.Rendering;

namespace Kiji.Generation;

internal sealed record IncrementalBuildPlan(
    IReadOnlyList<PageRenderRequest> PagesToRender,
    IReadOnlyList<BuildManifestPage> CarriedPages,
    BuildManifest? OldManifest,
    string OptionsHash,
    IReadOnlyList<string> CodeDependencies);
