using Kiji.Markdown;
using Kiji.Rendering;
using Kiji.Tests.TestSite;

namespace Kiji.Tests;

internal static class TestArticleContents
{
    public static SiteInfo CreateSiteInfo()
    {
        return new SiteInfo
        {
            BaseUrl = new Uri("https://example.com"),
            Name = "zzzkan.me",
            Description = "zzzkan.meです。",
            Language = "ja",
            Author = "zzzkan",
        };
    }

    public static ContentCollection<Post> CreateCatalog(params (Post Metadata, string Html)[] entries)
    {
        return ContentCollectionFromItems([.. entries.Select(static entry => ClonePost(entry.Metadata, entry.Html))]);
    }

    public static ContentCollection<Post> CreateCatalog(IEnumerable<Post> posts)
    {
        ArgumentNullException.ThrowIfNull(posts);

        return ContentCollectionFromItems([.. posts.Select(static post => ClonePost(post, $"<p>{post.Title}</p>"))]);
    }

    /// <summary>
    /// Creates a KijiApp wired exactly like the real site (pages, not-found, content
    /// and tag route mappings) over the given posts.
    /// </summary>
    public static (KijiApp App, ContentCollection<Post> Posts) CreateApp(params (Post Metadata, string Html)[] entries)
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = CreateSiteInfo();

        IReadOnlyList<Post> items = [.. entries.Select(static entry => ClonePost(entry.Metadata, entry.Html))];
        var posts = builder.AddContentSource<Post>(_ => items).WithKey(static post => post.Slug);

        var app = builder.Build();
        MapSite(app, posts);
        return (app, posts);
    }

    public static KijiApp MapSite(KijiApp app, ContentCollection<Post> posts)
    {
        app.MapPages<Root>();
        app.MapNotFound<NotFoundPage>();

        app.MapContent<PostPage, Post>(posts, static post => new { post.Slug });
        app.MapRoutes<TagsPage>(() => Slug.CreateNameMap(
                posts.Items.SelectMany(static post => post.Tags.Select(static tag => tag.Name)),
                "tag",
                "Tags")
            .OrderBy(static pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new { TagSlug = pair.Key }));

        return app;
    }

    public static IReadOnlyList<PageRenderRequest> CreatePageRequests(params (Post Metadata, string Html)[] entries)
    {
        var (app, _) = CreateApp(entries);
        return app.CreateSnapshot().Pages;
    }

    public static Post CreatePost(
        string slug,
        string title,
        string description,
        DateOnly createdAt,
        DateOnly? updatedAt = null,
        params string[] tags)
    {
        return Post.Create(CreateMarkdownContent(slug, title, description, ToDateTimeOffset(createdAt), updatedAt.HasValue ? ToDateTimeOffset(updatedAt.Value) : null, $"<p>{title}</p>", tags));
    }

    private static ContentCollection<Post> ContentCollectionFromItems(IReadOnlyList<Post> posts)
    {
        return Content.FromItems(posts).WithKey(static post => post.Slug);
    }

    private static Post ClonePost(Post post, string html)
    {
        ArgumentNullException.ThrowIfNull(post);

        return Post.Create(CreateMarkdownContent(
            post.Slug,
            post.Title,
            post.Description,
            post.CreatedAt,
            post.UpdatedAt,
            html,
            [.. post.Tags.Select(static tag => tag.Name)]));
    }

    private static MarkdownContent<FrontMatter> CreateMarkdownContent(
        string slug,
        string title,
        string description,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt,
        string html,
        params string[] tags)
    {
        var frontMatter = new FrontMatter
        {
            Title = title,
            Description = description,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Tags = [.. tags],
        };

        return new MarkdownContent<FrontMatter>(
            new MarkdownFileInfo(
                @"C:\test-contents",
                Path.Combine(@"C:\test-contents", slug + ".md"),
                slug + ".md",
                string.Empty,
                slug,
                new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc)),
            frontMatter,
            (_, _) => Task.FromResult(html));
    }

    private static DateTimeOffset ToDateTimeOffset(DateOnly date)
    {
        return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
    }
}
