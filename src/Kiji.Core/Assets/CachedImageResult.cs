using Kiji.Incremental;

namespace Kiji.Assets;

internal sealed record CachedImageResult(ProcessedImageInfo Info, IReadOnlyList<BuildManifestOutput> Outputs);
