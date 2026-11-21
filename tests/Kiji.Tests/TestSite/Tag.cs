namespace Kiji.Tests.TestSite;

public sealed class Tag
{
    public required string Name { get; init; }

    public required string UrlSlug { get; init; }

    public bool IsHighlighted { get; init; }
}
