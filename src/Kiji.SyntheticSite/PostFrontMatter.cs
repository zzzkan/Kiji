namespace Kiji.SyntheticSite;

/// <summary>
/// Front matter shape for the synthetic posts. Mirrors a typical blog site.
/// </summary>
public sealed class PostFrontMatter
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public List<string> Tags { get; set; } = [];
}
