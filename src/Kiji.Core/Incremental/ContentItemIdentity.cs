namespace Kiji.Incremental;

internal sealed record ContentItemIdentity(string SourceId, string Id, string? Digest, long Generation, bool Shared = false)
{
    internal string DependencyKey => SourceId + "/" + Id.ToUpperInvariant();
}
