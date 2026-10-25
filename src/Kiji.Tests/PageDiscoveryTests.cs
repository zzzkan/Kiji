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
    public void Create_DiscoversFixedRoutesWithoutRegistration()
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory), typeof(HomePage), typeof(AboutPage));
        builder.Info = TestArticleContents.CreateSiteInfo();
        var app = builder.Build();

        Assert.Equal(["/", "/about/"], app.CreateSnapshot().Pages.Select(static page => page.RoutePath).Order(StringComparer.Ordinal));
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
    public void CreateSnapshot_UnregisteredParameterizedRoute_FailsWithPageAndRoute()
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory), typeof(TagsPage));
        builder.Info = TestArticleContents.CreateSiteInfo();
        builder.AddContentSource<Post>(static _ => []);
        builder.UseDefaultLayout<MainLayout>();
        TestArticleContents.MapTestAssemblyPages(builder);
        builder.AddNotFoundPage<NotFoundPage>();
        builder.AddPageParameters<PostPage>(static services => services.GetRequiredService<ContentDictionary<Post>>()
            .Select(static post => new { post.Value.Slug, ContentKey = post.Key }));
        // No mapping for the dynamic /tags/{TagSlug}/ template.
        var app = builder.Build();

        var error = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());
        Assert.Contains(nameof(TagsPage), error.Message, StringComparison.Ordinal);
        Assert.Contains("/tags/{TagSlug}/", error.Message, StringComparison.Ordinal);
        Assert.Contains("AddPageParameters", error.Message, StringComparison.Ordinal);
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

        Assert.Contains("not a declared", exception.Message, StringComparison.Ordinal);
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
    public void CreateSnapshot_AddNotFoundPageWithoutPageTemplate_ThrowsInformativeException()
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.Info = TestArticleContents.CreateSiteInfo();
        builder.UseDefaultLayout<MainLayout>();
        TestArticleContents.MapTestAssemblyPages(builder);
        builder.AddNotFoundPage<PostListComponent>();
        var app = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());

        Assert.Contains("does not declare a '@page' route template", exception.Message, StringComparison.Ordinal);
    }

    private static (StaticSite App, ContentDictionary<Post> Posts) CreateAppWithTagRoutes(Func<IEnumerable<object>> tagRoutes)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.Info = TestArticleContents.CreateSiteInfo();
        builder.AddContentSource<Post>(static _ => []);
        builder.UseDefaultLayout<MainLayout>();
        TestArticleContents.MapTestAssemblyPages(builder);
        builder.AddNotFoundPage<NotFoundPage>();
        builder.AddPageParameters<PostPage>(static services => services.GetRequiredService<ContentDictionary<Post>>()
            .Select(static post => new { post.Value.Slug, ContentKey = post.Key }));
        builder.AddPageParameters<TagsPage>(_ => tagRoutes());
        var app = builder.Build();
        return (app, app.ServiceProvider.GetRequiredService<ContentDictionary<Post>>());
    }
}
