using Kiji.Incremental;

namespace Kiji.Images;

internal sealed record CachedImageResult(ProcessedImageInfo Info, IReadOnlyList<BuildManifestOutput> Outputs);
