using Kiji.Markdown;

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
            throw new InvalidOperationException($"Missing title in front matter: {content.FileInfo.FilePath}");
        }

        if (frontMatter.CreatedAt is null)
        {
            throw new InvalidOperationException($"Missing createdAt in front matter: {content.FileInfo.FilePath}");
        }

        var slug = CreateSlug(content.FileInfo);
        var tags = (frontMatter.Tags ?? [])
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .Select(static tag => new Tag
            {
                Name = tag,
                UrlSlug = global::Kiji.Slug.Create(tag).Value,
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

    private static string CreateSlug(MarkdownFileInfo fileInfo)
    {
        ArgumentNullException.ThrowIfNull(fileInfo);

        if (string.IsNullOrWhiteSpace(fileInfo.Slug))
        {
            throw new InvalidOperationException($"Cannot determine slug for markdown file: {fileInfo.FilePath}");
        }

        return global::Kiji.Slug.Normalize(fileInfo.Slug);
    }
}