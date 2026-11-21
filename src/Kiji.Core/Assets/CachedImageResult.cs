using Kiji.Generation;

namespace Kiji.Assets;

internal sealed record CachedImageResult(ProcessedImageInfo Info, IReadOnlyList<BuildManifestOutput> Outputs);
