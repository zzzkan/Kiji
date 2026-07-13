namespace Kiji.Generation;

/// <summary>
/// One fingerprinted input of a page render. <c>Kind</c> is <c>file</c> (a single
/// source file, key = root-relative path) or <c>content-set</c> (the digest of every
/// markdown file under the contents directory).
/// </summary>
internal sealed record BuildManifestDependency(string Kind, string Key, string Fingerprint)
{
    internal const string FileKind = "file";

    internal const string ContentSetKind = "content-set";
}
