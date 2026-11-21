using Kiji.Routing;
using Xunit;

namespace Kiji.Tests;

public sealed class StaticPagePlannerTests
{
    [Theory]
    [InlineData("/blog/{slug:alpha}/", "route constraints")]
    [InlineData("/blog/{slug?}/", "optional parameters")]
    [InlineData("/blog/{*slug}/", "catch-all parameters")]
    [InlineData("/blog/archive-{slug}/", "composite segment")]
    public void UnsupportedRouteSyntax_FailsWithTemplateAndReason(string template, string reason)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => StaticPageDefinition.Create(template));
        Assert.Contains(template, exception.Message, StringComparison.Ordinal);
        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }
}
