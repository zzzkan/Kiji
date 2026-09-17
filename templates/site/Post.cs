using Kiji.Markdown;

namespace KijiSite;

/// <summary>A post with its site-specific route and presentation metadata.</summary>
public sealed class Post
{
    private readonly MarkdownContent<PostFrontMatter> _content;

    private Post(MarkdownContent<PostFrontMatter> content, string slug)
    {
        _content = content;
        Slug = slug;
    }

    public string Slug { get; }

    public string? Title => _content.FrontMatter.Title;

    public string? Description => _content.FrontMatter.Description;

    public DateTimeOffset? CreatedAt => _content.FrontMatter.CreatedAt;

    public ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)
        => _content.RenderAsync(cancellationToken);

    public static Post Create(MarkdownContent<PostFrontMatter> content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var value = string.Equals(content.FileInfo.Name, "index.md", StringComparison.OrdinalIgnoreCase)
            ? content.FileInfo.Directory?.Name
            : Path.GetFileNameWithoutExtension(content.FileInfo.Name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Cannot determine a slug for '{content.FileInfo.FullName}'.");
        }

        return new Post(content, Kiji.Slug.Normalize(value));
    }
}
