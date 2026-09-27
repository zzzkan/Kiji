using Kiji.Assets;
using Kiji.Feeds;
using Kiji.Tests.TestSite.PageServices;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class PageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-services-{Guid.NewGuid():N}");

    [Fact]
    public async Task Renders_ShareWithinPage_IsolateConcurrentPages_AndDispose()
    {
        var probe = new ServiceProbe { Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var app = CreateApp(probe);
        app.AddPages<ServicePage>(_ => [new { Key = "first" }, new { Key = "second" }]);
        var requests = app.CreateSnapshot().Pages;

        var renders = requests.Select(request => app.RenderPageAsync(request, CancellationToken.None)).ToArray();
        try
        {
            // Both renders reach their asynchronous lifecycle before either can finish.
            Assert.Equal(2, probe.Created.Count);
            Assert.All(probe.Created, service => Assert.Equal(0, service.DisposeCount));
        }
        finally
        {
            probe.Release.SetResult();
        }
        await Task.WhenAll(renders);

        probe.Release = null;
        await app.RenderPageAsync(requests[0], CancellationToken.None);
        Assert.Equal(3, probe.Created.Count);
        Assert.Equal(3, probe.Created.Distinct().Count());
        Assert.Equal(3, probe.Created.Select(service => service.Page).Distinct().Count());
        foreach (var service in probe.Created)
        {
            Assert.Equal(1, service.DisposeCount);
            Assert.Equal(1, service.Dependency.DisposeCount);
            var places = probe.Reads.Where(read => ReferenceEquals(read.Service, service)).Select(read => read.Place);
            Assert.Contains("page", places);
            Assert.Contains("layout", places);
            Assert.Contains("child", places);
            Assert.Equal(new Uri(app.Info.BaseUrl, $"services/{service.Key}/"), service.Page.Url);
            var pageReads = probe.PageReads.Where(read => ReferenceEquals(read.Service, service)).ToArray();
            Assert.Equal(3, pageReads.Length);
            Assert.All(pageReads, read => Assert.Same(service.Page, read.Page));
        }
        Assert.Contains("firstfirst", await renders[0], StringComparison.Ordinal);
        Assert.Contains("secondsecond", await renders[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedRender_DisposesBothAsyncAndSyncServices()
    {
        var probe = new ServiceProbe();
        await using var app = CreateApp(probe);
        app.AddPages<ServicePage>(_ => [new { Key = "failure", Fail = true }]);
        var request = Assert.Single(app.CreateSnapshot().Pages);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.RenderPageAsync(request, CancellationToken.None));
        var service = Assert.Single(probe.Created);
        Assert.Equal(1, service.DisposeCount);
        Assert.Equal(1, service.Dependency.DisposeCount);
    }

    [Fact]
    public async Task Registration_RejectsInvalidAndDuplicateTypes_AndManagedServices()
    {
        await using var app = CreateApp();
        Assert.Throws<ArgumentException>(() => app.AddPageService<IDisposable>());
        Assert.Throws<ArgumentException>(() => app.AddPageService<LayoutComponentBase>());
        Assert.Throws<ArgumentException>(() => app.AddPageService<StaticSite>());
        Assert.Throws<ArgumentException>(() => app.AddPageService<ContentDictionary<ServiceProbe>>());
        Assert.Throws<ArgumentException>(() => app.AddPageService<IRenderService, IRenderService>());
        Assert.Throws<ArgumentException>(() => app.AddPageService<object, LayoutComponentBase>());
        Assert.Throws<ArgumentException>(() => app.AddPageService<object, StaticSite>());
        app.AddPageService<RenderDependency>();
        Assert.Throws<InvalidOperationException>(() => app.AddPageService<RenderDependency>());
        Assert.Throws<InvalidOperationException>(() => app.AddPageService<RenderDependency, RenderDependency>());
        app.AddPageService<object, RenderDependency>();
        Assert.Throws<InvalidOperationException>(() => app.AddPageService<object>());
        Assert.Throws<InvalidOperationException>(() => app.AddPageService<object, MissingDependencyService>());
        app.AddPageService<SiteInfo>();
        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());
        Assert.Contains(nameof(SiteInfo), exception.Message, StringComparison.Ordinal);
        Assert.Contains("managed by Kiji", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InterfaceRegistration_CannotReplaceManagedService()
    {
        await using var app = CreateApp();
        app.AddPageService<IImageProcessor, TrackingImageProcessor>();
        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());
        Assert.Contains(nameof(IImageProcessor), exception.Message, StringComparison.Ordinal);
        Assert.Contains("managed by Kiji", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingDependency_FailsBeforeRendering()
    {
        await using var app = CreateApp();
        app.AddPageService<object, MissingDependencyService>();
        var exception = Assert.Throws<AggregateException>(() => app.CreateSnapshot());
        Assert.Contains(nameof(MissingDependencyService), exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("route")]
    [InlineData("feed")]
    [InlineData("artifact")]
    [InlineData("loader")]
    public async Task PageServices_CannotBeResolvedOutsidePage(string consumer)
    {
        await using var app = CreateApp();
        app.AddPageService<IDisposable, RenderDependency>();
        switch (consumer)
        {
            case "route":
                app.AddPages<ServicePage>(provider =>
                {
                    _ = provider.GetRequiredService<IDisposable>();
                    return [new { Key = "test" }];
                });
                break;
            case "feed":
                app.AddRssFeed(provider =>
                {
                    _ = provider.GetRequiredService<IDisposable>();
                    return [];
                });
                break;
            case "artifact":
                app.AddArtifact("service.txt", static (_, context, _) =>
                {
                    _ = context.Services.GetRequiredService<IDisposable>();
                    return Task.CompletedTask;
                });
                break;
            case "loader":
                app.UseContentSource<ServiceProbe>(provider =>
                {
                    _ = provider.GetRequiredService<IDisposable>();
                    return [];
                });
                app.AddPages<ServicePage>(provider =>
                {
                    _ = provider.GetRequiredService<ContentDictionary<ServiceProbe>>().Count;
                    return [];
                });
                break;
        }
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => app.PublishAsync(Path.Combine(_root, "dist")));
        Assert.Contains("scoped service", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(IDisposable), exception.ToString(), StringComparison.Ordinal);
    }

    private StaticSite CreateApp(ServiceProbe? probe = null)
    {
        var app = StaticSite.Create([]);
        app.Info = new SiteInfo { Name = "Services", BaseUrl = new Uri("https://example.test/") };
        app.Paths.RootDirectory = _root;
        if (probe is not null)
        {
            app.UseContentSource<ServiceProbe>(_ => [probe]);
            app.AddPageService<RenderDependency>().AddPageService<IRenderService, RenderService>();
            app.UseDefaultLayout<ServiceLayout>();
        }
        return app;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
