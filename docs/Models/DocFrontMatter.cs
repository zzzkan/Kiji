namespace Kiji.Docs.Models;

/// <summary>
/// Front matter for a documentation page. Kiji does not define this shape — the site
/// does — so ordering and navigation titles live here rather than in the framework.
/// </summary>
public sealed class DocFrontMatter
{
    /// <summary>The page heading and <c>&lt;title&gt;</c>.</summary>
    public string? Title { get; set; }

    /// <summary>The meta description, and the blurb shown in the docs index.</summary>
    public string? Description { get; set; }

    /// <summary>Position in the navigation. Lower sorts first.</summary>
    public int Order { get; set; }
}