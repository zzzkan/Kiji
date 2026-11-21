namespace Kiji.Generation;

internal sealed record BuildManifestDependency(
    string Kind,
    string Key,
    string Fingerprint)
{
    internal const string FileKind = "file";
}
