using Kiji.Markdown;
using System.Text.RegularExpressions;

namespace Kiji.Docs.Models;

/// <summary>A documentation page with its site-specific route and presentation metadata.</summary>
public sealed class Article
{
    private readonly MarkdownContent<FrontMatter> _content;

    private Article(MarkdownContent<FrontMatter> content, string slug)
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

    public static Article Create(MarkdownContent<FrontMatter> content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var value = string.Equals(content.FileInfo.Name, "index.md", StringComparison.OrdinalIgnoreCase)
            ? content.FileInfo.Directory?.Name
            : Path.GetFileNameWithoutExtension(content.FileInfo.Name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Cannot determine a slug for '{content.FileInfo.FullName}'.");
        }

        return new Article(content, NormalizeSlug(value));
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
