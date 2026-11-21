namespace Kiji.Docs.Models;

public sealed class FrontMatter
{
    public required string Title { get; init; }

    public string? NavTitle { get; init; }

    public required string Description { get; init; }

    public int Order { get; init; }

    public required string Section { get; init; }
}
