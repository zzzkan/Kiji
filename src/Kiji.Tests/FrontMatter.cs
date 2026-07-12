namespace Kiji.Tests;

/// <summary>
/// The site-defined YAML front matter shape used by the test site. Kiji itself is
/// front-matter-shape agnostic; each site declares its own type.
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
