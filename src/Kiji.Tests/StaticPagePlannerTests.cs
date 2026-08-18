using Kiji.Routing;
using Xunit;

namespace Kiji.Tests;

public sealed class StaticPagePlannerTests
{
    [Fact]
    public void PlanPages_DynamicPagesBindOriginalParametersAndEncodedSegments()
    {
        var requests = StaticPagePlanner.PlanPages(
        [
            StaticPageDefinition.Create("/blog/{Slug}/"),
            StaticPageDefinition.Create("/tags/{TagSlug}/"),
        ],
        new Dictionary<string, IReadOnlyList<StaticPageRouteEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            ["/blog/{Slug}/"] =
            [
                new StaticPageRouteEntry(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Slug"] = "hello-world",
                    }),
            ],
            ["/tags/{TagSlug}/"] =
            [
                new StaticPageRouteEntry(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["TagSlug"] = "c-sharp-basics",
                    }),
            ],
        });

        var blogRequest = Assert.Single(
            requests,
            static request =>
                request.SourceIdentifier == "/blog/{Slug}/" &&
                string.Equals(request.Parameters["Slug"], "hello-world", StringComparison.Ordinal));
        var tagRequest = Assert.Single(
            requests,
            static request =>
                request.SourceIdentifier == "/tags/{TagSlug}/" &&
                string.Equals(request.Parameters["TagSlug"], "c-sharp-basics", StringComparison.Ordinal));

        Assert.Equal("hello-world", blogRequest.Parameters["Slug"]);
        Assert.Equal("/blog/hello-world/", blogRequest.RoutePath);
        Assert.Equal(Path.Combine("blog", "hello-world", "index.html"), blogRequest.OutputRelativePath);

        Assert.Equal("c-sharp-basics", tagRequest.Parameters["TagSlug"]);
        Assert.Equal("/tags/c-sharp-basics/", tagRequest.RoutePath);
        Assert.Equal(Path.Combine("tags", "c-sharp-basics", "index.html"), tagRequest.OutputRelativePath);
    }

    [Fact]
    public void StaticPageDefinition_CreateRouteConstraint_ThrowsInformativeException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StaticPageDefinition.Create("/blog/{slug:alpha}/"));

        Assert.Contains("/blog/{slug:alpha}/", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unsupported route constraints", exception.Message, StringComparison.Ordinal);
        Assert.Contains("simple route parameters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPageDefinition_CreateOptionalParameter_ThrowsInformativeException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StaticPageDefinition.Create("/blog/{slug?}/"));

        Assert.Contains("/blog/{slug?}/", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unsupported optional parameters", exception.Message, StringComparison.Ordinal);
        Assert.Contains("simple route parameters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPageDefinition_CreateCatchAllParameter_ThrowsInformativeException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StaticPageDefinition.Create("/blog/{*slug}/"));

        Assert.Contains("/blog/{*slug}/", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unsupported catch-all parameters", exception.Message, StringComparison.Ordinal);
        Assert.Contains("simple route parameters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPageDefinition_CreateCompositeSegment_ThrowsInformativeException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StaticPageDefinition.Create("/blog/archive-{slug}/"));

        Assert.Contains("/blog/archive-{slug}/", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unsupported composite segment syntax", exception.Message, StringComparison.Ordinal);
        Assert.Contains("simple route parameters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanPages_RegisteredRouteWithoutComponent_ThrowsInformativeException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StaticPagePlanner.PlanPages(
                [StaticPageDefinition.Create("/blog/{Slug}/")],
                new Dictionary<string, IReadOnlyList<StaticPageRouteEntry>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["/missing/{Slug}/"] = [],
                }));

        Assert.Contains("/blog/{Slug}/", exception.Message, StringComparison.Ordinal);
        Assert.Contains("static route provider", exception.Message, StringComparison.Ordinal);
    }
}
