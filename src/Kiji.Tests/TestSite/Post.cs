using Kiji.Markdown;
using System.Text.RegularExpressions;

namespace Kiji.Tests.TestSite;

public sealed class Post
{
    private readonly MarkdownContent<FrontMatter> _content;

    internal Post(
        MarkdownContent<FrontMatter> content,
        string slug,
        string slugUrlEncoded,
        string title,
        string description,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt,
        IReadOnlyList<Tag> tags)
    {
        _content = content;
        Slug = slug;
        SlugUrlEncoded = slugUrlEncoded;
        Title = title;
        Description = description;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Tags = tags;
    }

    /// <summary>The slug is what routes and feeds correlate on, so it is the key.</summary>
    public string Key => Slug;

    public string Slug { get; }

    public string SlugUrlEncoded { get; }

    public string Title { get; }

    public string Description { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? UpdatedAt { get; }

    public IReadOnlyList<Tag> Tags { get; }

    public ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)
    {
        return _content.RenderAsync(cancellationToken);
    }

    internal static Post Create(MarkdownContent<FrontMatter> content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var frontMatter = content.FrontMatter;
        if (string.IsNullOrWhiteSpace(frontMatter.Title))
        {
            throw new InvalidOperationException($"Missing title in front matter: {content.FileInfo.FullName}");
        }

        if (frontMatter.CreatedAt is null)
        {
            throw new InvalidOperationException($"Missing createdAt in front matter: {content.FileInfo.FullName}");
        }

        var slug = CreateSlug(content.FileInfo);
        var tags = (frontMatter.Tags ?? [])
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .Select(static tag => new Tag
            {
                Name = tag,
                UrlSlug = NormalizeSlug(tag),
            })
            .ToArray();

        return new Post(
            content,
            slug,
            string.Join('/', slug.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString)),
            frontMatter.Title,
            frontMatter.Description ?? string.Empty,
            frontMatter.CreatedAt.Value,
            frontMatter.UpdatedAt,
            tags);
    }

    private static string CreateSlug(FileInfo fileInfo)
    {
        ArgumentNullException.ThrowIfNull(fileInfo);

        var value = string.Equals(fileInfo.Name, "index.md", StringComparison.OrdinalIgnoreCase)
            ? fileInfo.Directory?.Name
            : Path.GetFileNameWithoutExtension(fileInfo.Name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Cannot determine slug for markdown file: {fileInfo.FullName}");
        }

        return NormalizeSlug(value);
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
