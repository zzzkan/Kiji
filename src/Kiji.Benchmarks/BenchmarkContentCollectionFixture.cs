using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Benchmarks;

internal static class BenchmarkContentCollectionFixture
{
    internal static ContentCollection<T> FromItems<T>(IReadOnlyList<T> items, Func<T, string> id)
        where T : class
    {
        var builder = StaticSite.CreateBuilder([], new SiteExecutionPaths(AppContext.BaseDirectory), typeof(BenchmarkContentCollectionFixture).Assembly);
        builder.SiteInfo = new SiteInfo { Name = "Fixture", BaseUrl = new Uri("https://example.test/") };
        builder.RegisterContent<T>(_ => [.. items.Select(item => (id(item), item, Digest: (string?)null))]);
        return builder.Build().ServiceProvider.GetRequiredService<ContentCollection<T>>();
    }
}
