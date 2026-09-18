namespace Kiji.Docs.Models;

/// <summary>
/// Front matter for a documentation page.
/// </summary>
public sealed class FrontMatter
{
    /// <summary>The page heading and <c>&lt;title&gt;</c>.</summary>
    public string? Title { get; set; }

    /// <summary>The meta description, and the blurb shown in the docs index.</summary>
    public string? Description { get; set; }

    /// <summary>Position in the navigation. Lower sorts first.</summary>
    public int Order { get; set; }
}
