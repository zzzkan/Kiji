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

        var slugSource = string.Equals(fileInfo.FileNameWithoutExtension, "index", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(fileInfo.RelativeDirectoryPath)
            : fileInfo.FileNameWithoutExtension;

        if (string.IsNullOrWhiteSpace(slugSource))
        {
            throw new InvalidOperationException($"Cannot determine slug for markdown file: {fileInfo.FilePath}");
        }

        return global::Kiji.Slug.Normalize(slugSource);
    }
}

public sealed class Tag
{
    public required string Name { get; init; }

    public required string UrlSlug { get; init; }

    public bool IsHighlighted { get; init; }
}