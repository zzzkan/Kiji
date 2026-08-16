namespace KijiSite;

/// <summary>
/// The YAML front matter shape for posts. Kiji does not define this — each site
/// declares whatever fields it wants, and unknown keys are ignored.
/// </summary>
public sealed class PostFrontMatter
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public List<string> Tags { get; set; } = [];
}
