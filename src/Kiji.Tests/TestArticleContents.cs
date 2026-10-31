using Kiji.Markdown;
using Kiji.Rendering;
using Kiji.Tests.TestSite.Pages;
using Kiji.Tests.TestSite;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Tests;

internal static class TestArticleContents
{
    public static SiteInfo CreateSiteInfo()
    {
        return CreateSiteInfo(new Uri("https://example.com"));
    }

    /// <summary>
    /// The same site metadata published under a sub-path, for exercising base-path
    /// behavior. Kept separate from <see cref="CreateSiteInfo()"/> because most tests
    /// assert against the domain-root URLs.
    /// </summary>
    public static SiteInfo CreateSiteInfoWithBasePath()
    {
        return CreateSiteInfo(new Uri("https://example.com/kiji/"));
    }

    private static SiteInfo CreateSiteInfo(Uri baseUrl)
    {
        return new SiteInfo
        {
            BaseUrl = baseUrl,
            Name = "zzzkan.me",
            Description = "zzzkan.meです。",
            Language = "ja",
            Author = "zzzkan",
        };
    }

    /// <summary>
    /// Builds a site wired exactly like the real site (pages, not-found, content
    /// and tag route mappings) over the given posts.
    /// </summary>
    private static StaticSite CreateApp(params (Post Metadata, string Html)[] entries)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.SiteInfo = CreateSiteInfo();

        IReadOnlyList<Post> items = [.. entries.Select(static entry => ClonePost(entry.Metadata, entry.Html))];
        builder.RegisterContent<Post>(_ => [.. items.Select(post => (post.Id, post, Digest: (string?)null))]);
        MapSite(builder);
        return builder.Build();
    }

    /// <summary>
    /// Includes the shared fixture's fixed pages; callers supply dynamic parameters explicitly.
    /// </summary>
    public static StaticSiteBuilder MapTestAssemblyPages(StaticSiteBuilder builder)
    {
        builder.AddPageParameters<HomePage>(static _ => [new { }]);
        builder.AddPageParameters<AboutPage>(static _ => [new { }]);
        builder.AddPageParameters<PrivacyPolicyPage>(static _ => [new { }]);
        builder.AddPageParameters<BlogIndexPage>(static _ => [new { }]);
        return builder;
    }

    public static StaticSiteBuilder MapSite(StaticSiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseDefaultLayout<MainLayout>();
        MapTestAssemblyPages(builder);
        builder.UseNotFoundPage<NotFoundPage>();

        builder.AddPageParameters<PostPage>(static services => services.GetRequiredService<ContentCollection<Post>>()
            .Select(static post => new { post.Slug, ContentKey = post.Id }));
        builder.AddPageParameters<TagsPage>(static services => services.GetRequiredService<ContentCollection<Post>>()
            .SelectMany(static post => post.Tags.Select(static tag => tag.UrlSlug))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static slug => new { TagSlug = slug }));

        return builder;
    }

    public static IReadOnlyList<PageRenderRequest> CreatePageRequests(params (Post Metadata, string Html)[] entries)
    {
        var app = CreateApp(entries);
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
            new FileInfo(Path.Combine(@"C:\test-contents", slug + ".md")),
            frontMatter,
            string.Empty,
            (_, _) => Task.FromResult(html));
    }

    private static DateTimeOffset ToDateTimeOffset(DateOnly date)
    {
        return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
    }
}
