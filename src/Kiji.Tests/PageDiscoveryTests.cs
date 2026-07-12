using Xunit;
using Kiji.Tests.TestSite;

namespace Kiji.Tests;

/// <summary>
/// Tests for page discovery and route mapping through <see cref="KijiApp"/>.
/// </summary>
public sealed class PageDiscoveryTests
{
    [Fact]
    public void CreateSnapshot_RepresentativeStaticPagesResolveExpectedRoutes()
    {
        var requests = TestArticleContents.CreatePageRequests();

        var homeRequest = Assert.Single(requests, static request => request.SourceIdentifier == "/");
        var blogRequest = Assert.Single(requests, static request => request.SourceIdentifier == "/blog/");
        var aboutRequest = Assert.Single(requests, static request => request.SourceIdentifier == "/about/");
        var privacyRequest = Assert.Single(requests, static request => request.SourceIdentifier == "/privacy-policy/");
        var notFoundRequest = Assert.Single(requests, static request => request.SourceIdentifier == "/not-found/");

        Assert.Equal("/", homeRequest.RoutePath);
        Assert.Equal("index.html", homeRequest.OutputRelativePath);
        Assert.Equal("/blog/", blogRequest.RoutePath);
        Assert.Equal(Path.Combine("blog", "index.html"), blogRequest.OutputRelativePath);
        Assert.Equal("/about/", aboutRequest.RoutePath);
        Assert.Equal("/privacy-policy/", privacyRequest.RoutePath);
        Assert.Equal("/404.html", notFoundRequest.RoutePath);
        Assert.Equal("404.html", notFoundRequest.OutputRelativePath);
        Assert.True(notFoundRequest.ExcludeFromSitemap);
        Assert.DoesNotContain(requests, static request => request.SourceIdentifier.StartsWith("/Pages/", StringComparison.Ordinal));
    }

    [Fact]
    public void FromTypes_SkipsCompilerGeneratedTypes()
    {
        // Closures report the namespace of their declaring type, so the documented
        // namespace-filtered assembly.GetTypes() idiom must not trip over them.
        var compilerGenerated = typeof(TestArticleContents).Assembly.GetTypes()
            .Where(static type => type.IsDefined(
                typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute),
                inherit: false))
            .Take(3);

        var discovered = Kiji.Routing.PageDiscovery.FromTypes([.. TestSitePages.All, .. compilerGenerated]);

        Assert.Equal(
            TestSitePages.All.Length,
            discovered.Select(static page => page.ComponentType).Distinct().Count());
    }

    [Fact]
    public void CreateSnapshot_UsesBlogIndexAsTheOnlyBlogListingPage()
    {
        var requests = TestArticleContents.CreatePageRequests();

        var blogRequests = requests
            .Where(static request => string.Equals(request.RoutePath, "/blog/", StringComparison.Ordinal))
            .ToList();

        var blogRequest = Assert.Single(blogRequests);
        Assert.Equal("/blog/", blogRequest.SourceIdentifier);
    }

    [Fact]
    public void CreateSnapshot_DynamicPagesBindConfiguredSourceParameters()
    {
        var requests = TestArticleContents.CreatePageRequests(
            (TestArticleContents.CreatePost(
                "hello-world",
                "Hello",
                "desc",
                new DateOnly(2026, 3, 9),
                null,
                "C Sharp Basics"), "<p>Hello</p>"));

        var blogRequest = Assert.Single(
            requests,
            static request =>
                request.SourceIdentifier == "/blog/{Slug}/" &&
                string.Equals((string?)request.Parameters["Slug"], "hello-world", StringComparison.Ordinal));
        var tagRequest = Assert.Single(
            requests,
            static request =>
                request.SourceIdentifier == "/tags/{TagSlug}/" &&
                string.Equals((string?)request.Parameters["TagSlug"], "c-sharp-basics", StringComparison.Ordinal));

        Assert.Equal("hello-world", blogRequest.Parameters["Slug"]);
        Assert.Equal("/blog/hello-world/", blogRequest.RoutePath);
        Assert.Equal(Path.Combine("blog", "hello-world", "index.html"), blogRequest.OutputRelativePath);
        Assert.Equal("hello-world", blogRequest.AssociatedContentIdentity);

        Assert.Equal("c-sharp-basics", tagRequest.Parameters["TagSlug"]);
        Assert.Equal("/tags/c-sharp-basics/", tagRequest.RoutePath);
        Assert.Equal(Path.Combine("tags", "c-sharp-basics", "index.html"), tagRequest.OutputRelativePath);
    }

    [Fact]
    public void CreateSnapshot_TagSlugCollision_ThrowsInformativeException()
    {
        var (app, _) = TestArticleContents.CreateApp(
            (TestArticleContents.CreatePost("first-post", "First", "desc", new DateOnly(2026, 3, 9), null, "C Sharp"), "<p>First</p>"),
            (TestArticleContents.CreatePost("second-post", "Second", "desc", new DateOnly(2026, 3, 10), null, "C-Sharp"), "<p>Second</p>"));

        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());

        Assert.Contains("canonical tag slug 'c-sharp'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Rename one of the tags", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSnapshot_DynamicRouteWithoutMapping_ThrowsInformativeException()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        var posts = builder.AddContentSource<Post>(static _ => []).WithKey(static post => post.Slug);

        var app = builder.Build();
        app.MapRoot<Root>();
        app.MapPages(TestSitePages.All);
        app.MapNotFound<NotFoundPage>();
        app.MapContent<PostPage, Post>(posts, static post => new { post.Slug });
        // No mapping for the dynamic /tags/{TagSlug}/ template.

        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());

        Assert.Contains("require a static route provider", exception.Message, StringComparison.Ordinal);
        Assert.Contains("/tags/{TagSlug}/", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSnapshot_MappingWithUnexpectedParameter_ThrowsInformativeException()
    {
        var (app, _) = CreateAppWithTagRoutes(static () => [new { TagSlug = "x", Wrong = "value" }]);

        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());

        Assert.Contains("route values not declared", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Wrong'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("/tags/{TagSlug}/", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSnapshot_MappingWithMissingParameter_ThrowsInformativeException()
    {
        var (app, _) = CreateAppWithTagRoutes(static () => [new { }]);

        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());

        Assert.Contains("did not supply required route values", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'TagSlug'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSnapshot_MapRoutesOnStaticPage_ThrowsInformativeException()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();

        var app = builder.Build();
        app.MapRoot<Root>();
        app.MapPages(TestSitePages.All);
        app.MapRoutes<HomePage>(static () => [new { Slug = "x" }]);

        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());

        Assert.Contains("no dynamic '@page' route template", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSnapshot_MapNotFoundWithoutPageTemplate_ThrowsInformativeException()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();

        var app = builder.Build();
        app.MapRoot<Root>();
        app.MapPages(TestSitePages.All);
        app.MapNotFound<PostListComponent>();

        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());

        Assert.Contains("does not declare a '@page' route template", exception.Message, StringComparison.Ordinal);
    }

    private static (KijiApp App, ContentCollection<Post> Posts) CreateAppWithTagRoutes(Func<IEnumerable<object>> tagRoutes)
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        var posts = builder.AddContentSource<Post>(static _ => []).WithKey(static post => post.Slug);

        var app = builder.Build();
        app.MapRoot<Root>();
        app.MapPages(TestSitePages.All);
        app.MapNotFound<NotFoundPage>();
        app.MapContent<PostPage, Post>(posts, static post => new { post.Slug });
        app.MapRoutes<TagsPage>(tagRoutes);
        return (app, posts);
    }
}
