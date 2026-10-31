using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Tests;

internal static class ContentCollectionFixture
{
    internal static ContentCollection<T> FromItems<T>(IReadOnlyList<T> items, Func<T, string> id)
        where T : class
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.SiteInfo = new SiteInfo { Name = "Fixture", BaseUrl = new Uri("https://example.test/") };
        builder.RegisterContent<T>(_ => [.. items.Select(item => (id(item), item, Digest: (string?)null))]);
        return builder.Build().ServiceProvider.GetRequiredService<ContentCollection<T>>();
    }
}
