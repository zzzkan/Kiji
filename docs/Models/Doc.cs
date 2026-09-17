using Kiji.Markdown;

namespace Kiji.Docs.Models;

/// <summary>A documentation page with its site-specific route and presentation metadata.</summary>
public sealed class Doc
{
    private readonly MarkdownContent<DocFrontMatter> _content;

    private Doc(MarkdownContent<DocFrontMatter> content, string slug)
    {
        _content = content;
        Slug = slug;
    }

    public string Slug { get; }

    public string? Title => _content.FrontMatter.Title;

    public string? Description => _content.FrontMatter.Description;

    public int Order => _content.FrontMatter.Order;

    public ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)
    {
        return _content.RenderAsync(cancellationToken);
    }

    public static Doc Create(MarkdownContent<DocFrontMatter> content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var value = string.Equals(content.FileInfo.Name, "index.md", StringComparison.OrdinalIgnoreCase)
            ? content.FileInfo.Directory?.Name
            : Path.GetFileNameWithoutExtension(content.FileInfo.Name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Cannot determine a slug for '{content.FileInfo.FullName}'.");
        }

        return new Doc(content, Kiji.Slug.Normalize(value));
    }
}
