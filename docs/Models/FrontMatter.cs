namespace Kiji.Docs.Models;

/// <summary>
/// Front matter for a documentation page.
/// </summary>
public sealed class FrontMatter
{
    /// <summary>The page heading and <c>&lt;title&gt;</c>.</summary>
    public required string Title { get; init; }

    /// <summary>The meta description, and the blurb shown in the docs index.</summary>
    public required string Description { get; init; }

    /// <summary>Position in the navigation. Lower sorts first.</summary>
    public int Order { get; init; }
}
