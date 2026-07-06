namespace Kiji.Markdown;

/// <summary>
/// Represents the YAML front matter in a markdown file.
/// </summary>
public sealed class FrontMatter
{
    /// <summary>
    /// Gets or sets the title of the post.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the creation date of the post.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last update date of the post.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the description of the post.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the tags associated with the post.
    /// </summary>
    public List<string>? Tags { get; set; }
}
