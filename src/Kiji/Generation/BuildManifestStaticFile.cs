namespace Kiji.Generation;

/// <summary>
/// A synced static file: source and destination stamps (length + last write time).
/// The copy is skipped when both stamps still match the manifest.
/// </summary>
internal sealed record BuildManifestStaticFile(
    string RelativePath,
    long SourceLength,
    DateTime SourceLastWriteTimeUtc,
    long DestinationLength,
    DateTime DestinationLastWriteTimeUtc);
