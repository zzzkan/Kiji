namespace Kiji.Generation;

/// <summary>The input and destination needed to repair an image without rendering HTML.</summary>
internal sealed record BuildManifestImage(string Source, string OutputDirectory);
