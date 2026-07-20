namespace Kiji.Generation;

/// <summary>
/// One fingerprinted input of a page render. <c>Kind</c> is <c>file</c> (a single
/// source file, key = root-relative path) or <c>content-set</c> (the digest of every
/// markdown file under the contents directory). File dependencies also carry the
/// stamp (length + last write time) observed when the fingerprint was computed, so
/// later builds can trust the fingerprint without re-reading unchanged files.
/// </summary>
internal sealed record BuildManifestDependency(
    string Kind,
    string Key,
    string Fingerprint,
    long? Length = null,
    DateTime? LastWriteTimeUtc = null)
{
    internal const string FileKind = "file";

    internal const string ContentSetKind = "content-set";
}
