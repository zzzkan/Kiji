using Kiji.Tests.TestSite.Pages;
using Kiji.Tests.TestSite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Tests for page discovery and route mapping through <see cref="StaticSite"/>.
/// </summary>
public sealed class PageDiscoveryTests
{
    [Fact]
    public void CreateSnapshot_RepresentativeStaticPagesResolveExpectedRoutes()
    {
        var requests = TestArticleContents.CreatePageRequests();

        var homeRequest = Assert.Single(requests, static request => request.SourceIdentifier == "/");
        var blogRequest = Assert.Single(requests, static request => request.SourceIdentifier == "/blog/");

        Assert.Equal("/", homeRequest.RoutePath);
        Assert.Equal("index.html", homeRequest.OutputRelativePath);
        Assert.Equal("/blog/", blogRequest.RoutePath);
        Assert.Equal(Path.Combine("blog", "index.html"), blogRequest.OutputRelativePath);
    }

    [Fact]
    public void AddStaticPages_EntryAssembly_RegistersThePages()
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.UseContentSource<Post>(static _ => []);
        // Under the MTP runner the test project is its own executable, so the
        // entry assembly is Kiji.Tests itself.
        app.AddStaticPages();
        app.AddPages<PostPage>(static services => services.GetRequiredService<ContentDictionary<Post>>()
            .Select(static post => new { post.Value.Slug, ContentKey = post.Key }));

        var requests = app.CreateSnapshot().Pages;

        Assert.Contains(requests, static request => request.SourceIdentifier == "/");
        Assert.Contains(requests, static request => request.SourceIdentifier == "/about/");
    }

    [Fact]
    public void AddStaticPages_AssemblyWithoutRoutedComponents_ReturnsEmptySnapshot()
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();

        app.AddStaticPages(typeof(StaticSite).Assembly);
        Assert.Empty(app.CreateSnapshot().Pages);
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

        Assert.Equal("c-sharp-basics", tagRequest.Parameters["TagSlug"]);
        Assert.Equal("/tags/c-sharp-basics/", tagRequest.RoutePath);
        Assert.Equal(Path.Combine("tags", "c-sharp-basics", "index.html"), tagRequest.OutputRelativePath);
    }

    [Fact]
    public void CreateSnapshot_UnregisteredParameterizedRoute_IsIgnored()
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.UseContentSource<Post>(static _ => []);
        app.UseDefaultLayout<MainLayout>();
        TestArticleContents.MapTestAssemblyPages(app);
        app.UseNotFoundPage<NotFoundPage>();
        app.AddPages<PostPage>(static services => services.GetRequiredService<ContentDictionary<Post>>()
            .Select(static post => new { post.Value.Slug, ContentKey = post.Key }));
        // No mapping for the dynamic /tags/{TagSlug}/ template.

        var pages = app.CreateSnapshot().Pages;
        Assert.DoesNotContain(pages, page => page.ComponentType == typeof(TagsPage));
    }

    /// <summary>
    /// A mapping may supply values beyond the route template — they reach the component
    /// as ordinary parameters — but a name the component does not declare is a typo, and
    /// saying so at plan time beats a render-time failure with no mapping named.
    /// </summary>
    [Fact]
    public void CreateSnapshot_MappingWithUndeclaredParameter_ThrowsInformativeException()
    {
        var (app, _) = CreateAppWithTagRoutes(static () => [new { TagSlug = "x", Wrong = "value" }]);

        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());

        Assert.Contains("neither route parameters nor declared", exception.Message, StringComparison.Ordinal);
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
    public void CreateSnapshot_MappingWithPathSeparatorInRouteValue_ThrowsInformativeException()
    {
        var (app, _) = CreateAppWithTagRoutes(static () => [new { TagSlug = "nested/value" }]);

        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());

        Assert.Contains("supplied invalid route value 'TagSlug'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("nested/value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("single route segment", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSnapshot_MappingWithDotDotRouteValue_ThrowsInformativeException()
    {
        var (app, _) = CreateAppWithTagRoutes(static () => [new { TagSlug = ".." }]);

        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());

        Assert.Contains("supplied invalid route value 'TagSlug'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'..'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be '.' or '..'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSnapshot_UseNotFoundPageWithoutPageTemplate_ThrowsInformativeException()
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.UseDefaultLayout<MainLayout>();
        TestArticleContents.MapTestAssemblyPages(app);
        app.UseNotFoundPage<PostListComponent>();

        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());

        Assert.Contains("does not declare a '@page' route template", exception.Message, StringComparison.Ordinal);
    }

    private static (StaticSite App, ContentDictionary<Post> Posts) CreateAppWithTagRoutes(Func<IEnumerable<object>> tagRoutes)
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.UseContentSource<Post>(static _ => []);
        app.UseDefaultLayout<MainLayout>();
        TestArticleContents.MapTestAssemblyPages(app);
        app.UseNotFoundPage<NotFoundPage>();
        app.AddPages<PostPage>(static services => services.GetRequiredService<ContentDictionary<Post>>()
            .Select(static post => new { post.Value.Slug, ContentKey = post.Key }));
        app.AddPages<TagsPage>(_ => tagRoutes());
        return (app, app.ServiceProvider.GetRequiredService<ContentDictionary<Post>>());
    }
}
