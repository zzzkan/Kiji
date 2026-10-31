namespace Kiji.Hosting;

/// <summary>The identity and observed data of an item in one content snapshot.</summary>
internal sealed record ContentItemIdentity(string SourceId, string Id, string? Digest, long Generation)
{
    internal string DependencyKey => SourceId + "/" + Id.ToUpperInvariant();
}
