namespace Kiji.Hosting;

/// <summary>The identity and observed data of an item in one content snapshot.</summary>
/// <param name="Shared">One instance stands for several items, so it identifies none of them.</param>
internal sealed record ContentItemIdentity(string SourceId, string Id, string? Digest, long Generation, bool Shared = false)
{
    internal string DependencyKey => SourceId + "/" + Id.ToUpperInvariant();
}
