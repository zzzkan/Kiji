using Kiji.Markdown;
using System.Text.RegularExpressions;

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

        return new Post(content, NormalizeSlug(value));
    }

    private static string NormalizeSlug(string value)
    {
        var trimmed = value.Trim().Trim('/', '\\');
        if (trimmed.Length == 0 || trimmed.Contains('/') || trimmed.Contains('\\'))
        {
            throw new InvalidOperationException($"Slug '{value}' must be a non-empty single route segment.");
        }

        var normalized = Regex.Replace(trimmed.ToLowerInvariant(), @"[^a-z0-9\-]", "-");
        normalized = Regex.Replace(normalized, "-+", "-").Trim('-');
        return normalized.Length > 0
            ? normalized
            : throw new InvalidOperationException($"Slug '{value}' cannot be normalized to an empty value.");
    }
}
