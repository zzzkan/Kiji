using Kiji.Tests.TestSite.TypedParameters;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class FixedPageParametersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"KijiFixedParameters-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OmittedParameters_UseDefaults_WithoutEnforcingEditorRequired(bool explicitSet)
    {
        await using var site = CreateSite("defaults");
        if (explicitSet) { site.AddPageParameters<FixedParameterPage>(static _ => [new { }]); }
        var page = Assert.Single(site.CreateSnapshot().Pages);
        Assert.Equal("/fixed-parameters/", page.RoutePath);
        Assert.Contains("Default title|7|", await site.RenderPageAsync(page, CancellationToken.None), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuppliedParameters_ReplaceTheDefaultRendering()
    {
        await using var site = CreateSite("supplied");
        site.AddPageParameters<FixedParameterPage>(static _ => [new { Title = "Custom title", Count = 42, Description = (string?)null }]);
        var page = Assert.Single(site.CreateSnapshot().Pages);
        Assert.Contains("Custom title|42|", await site.RenderPageAsync(page, CancellationToken.None), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyParameters_ExplicitlyOmitTheFixedPage()
    {
        await using var site = CreateSite("empty");
        site.AddPageParameters<FixedParameterPage>(static _ => []);
        Assert.Empty(site.CreateSnapshot().Pages);
    }

    [Fact]
    public async Task MultipleParameterSets_CannotGenerateTheSameFixedUrl()
    {
        await using var site = CreateSite("duplicate");
        site.AddPageParameters<FixedParameterPage>(static _ => [new { Title = "First" }]);
        site.AddPageParameters<FixedParameterPage>(static _ => [new { Title = "Second" }]);
        var error = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("duplicate output path", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Unknown", "value", "not a declared")]
    [InlineData("Count", "42", "expected 'System.Int32'")]
    public async Task SuppliedParameters_AreValidatedOnFixedPages(string name, string value, string message)
    {
        await using var site = CreateSite("invalid");
        site.AddPageParameters<FixedParameterPage>(_ => [new Dictionary<string, object?> { [name] = value }]);
        var error = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParameterChanges_InvalidateReuse_AndEmptyResultsRemoveOutput()
    {
        var log = new FixedParameterLog();
        IEnumerable<object> parameters = [new { Title = "Before" }];
        await using var site = CreateSite("incremental", log);
        site.AddPageParameters<FixedParameterPage>(_ => parameters);
        var output = Path.Combine(_root, "incremental", "dist", "fixed-parameters", "index.html");

        await site.PublishAsync("dist");
        await site.PublishAsync("dist");
        Assert.Equal(1, log.Count);
        File.Delete(output);
        await site.PublishAsync("dist");
        Assert.Equal(1, log.Count);
        Assert.Contains("Before|7|", await File.ReadAllTextAsync(output), StringComparison.Ordinal);

        parameters = [new { Title = "After" }];
        await site.PublishAsync("dist");
        Assert.Equal(2, log.Count);
        await using var clean = CreateSite("clean");
        clean.AddPageParameters<FixedParameterPage>(_ => parameters);
        await clean.PublishAsync("dist");
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(_root, "clean", "dist", "fixed-parameters", "index.html")),
            await File.ReadAllBytesAsync(output));

        parameters = [];
        await site.PublishAsync("dist");
        Assert.False(File.Exists(output));
        Assert.Equal(2, log.Count);
    }

    private StaticSite CreateSite(string directory, FixedParameterLog? log = null)
    {
        var site = TestSiteFactory.Create([], new SiteExecutionPaths(Path.Combine(_root, directory)),
            unconfiguredPage: typeof(FixedParameterPage));
        site.Info = TestArticleContents.CreateSiteInfo();
        if (log is not null) { site.UseServices(services => services.AddSingleton(log)); }
        return site;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }
}
