namespace Kiji.Generation;

/// <summary>
/// One fingerprinted input of a page render. <c>Kind</c> is <c>file</c> (a single
/// source file, key = root-relative path) or <c>value</c> for a registered
/// content or external-value digest. Equivalence uses content hashes.
/// </summary>
internal sealed record BuildManifestDependency(
    string Kind,
    string Key,
    string Fingerprint)
{
    internal const string FileKind = "file";
}
