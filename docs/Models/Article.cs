using Kiji.Markdown;
using System.Text.RegularExpressions;

namespace Kiji.Docs.Models;

/// <summary>A documentation page with its site-specific route and presentation metadata.</summary>
public sealed class Article
{
    private readonly MarkdownContent<FrontMatter> _content;

    public Article(MarkdownContent<FrontMatter> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Path, nameof(content.Path));
        ArgumentException.ThrowIfNullOrWhiteSpace(content.FrontMatter.Title, nameof(content.FrontMatter.Title));
        ArgumentException.ThrowIfNullOrWhiteSpace(content.FrontMatter.Description, nameof(content.FrontMatter.Description));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(content.FrontMatter.Order, nameof(content.FrontMatter.Order));

        var slug = string.Equals(Path.GetFileName(content.Path), "index.md", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(content.Path))
            : Path.GetFileNameWithoutExtension(content.Path);
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new InvalidOperationException($"Cannot determine a slug for '{content.Path}'.");
        }

        _content = content;
        Slug = NormalizeSlug(slug);
    }

    public string Slug { get; }

    public string Title => _content.FrontMatter.Title;

    public string Description => _content.FrontMatter.Description;

    public int Order => _content.FrontMatter.Order;

    public ValueTask<string> RenderAsync(CancellationToken cancellationToken = default) =>
        _content.RenderAsync(cancellationToken);

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
