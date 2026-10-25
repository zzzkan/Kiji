using Kiji.Tests.TestSite.TypedParameters;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class FixedPageParametersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"KijiFixedParameters-{Guid.NewGuid():N}");

    [Fact]
    public async Task FixedPage_UsesDefaultsUntilParametersAreSupplied()
    {
        var log = new FixedParameterLog();
        var output = Path.Combine(_root, "dist", "fixed-parameters", "index.html");
        async Task PublishAsync(IEnumerable<object>? parameters = null)
        {
            var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_root), typeof(FixedParameterPage));
            builder.Info = TestArticleContents.CreateSiteInfo();
            builder.Services.AddSingleton(log);
            if (parameters is not null) { builder.AddPageParameters<FixedParameterPage>(_ => parameters); }
            await using var site = builder.Build();
            await site.PublishAsync("dist");
        }

        await PublishAsync();
        await PublishAsync();
        Assert.Equal(1, log.Count);
        Assert.Contains("Default title|7|", await File.ReadAllTextAsync(output), StringComparison.Ordinal);

        // Supplied sets replace the default rendering; omitted parameters, even EditorRequired ones, keep defaults.
        await PublishAsync([new { Title = "Supplied" }]);
        Assert.Equal(2, log.Count);
        Assert.Contains("Supplied|7|", await File.ReadAllTextAsync(output), StringComparison.Ordinal);

        await PublishAsync([]);
        Assert.False(File.Exists(output));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }
}
