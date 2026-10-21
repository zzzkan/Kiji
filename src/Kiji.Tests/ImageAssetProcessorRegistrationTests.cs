using Kiji.Assets;
using Kiji.Markdown;
using Kiji.Tests.TestSite.PageServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Kiji.Tests;

public sealed class ImageAssetProcessorRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-image-service-{Guid.NewGuid():N}");

    [Fact]
    public async Task Factory_IsLazy_LastWins_SharedAcrossReloads_AndDisposedWithSite()
    {
        var processor = new TrackingImageProcessor();
        var calls = 0;
        await using (var app = CreateApp())
        {
            app.UseServices(services =>
            {
                services.AddTransient<IImageProcessor>(_ => throw new InvalidOperationException("Superseded factory must not run."));
                services.AddSingleton<IImageProcessor>(provider =>
                {
                    Assert.Same(app.Info, provider.GetRequiredService<SiteInfo>());
                    calls++;
                    return processor;
                });
                // Keyed registrations are separate; only the unkeyed processor serves Kiji.
                services.AddKeyedScoped<IImageProcessor>("other", (_, _) => throw new InvalidOperationException("Keyed factory must not run."));
            });
            app.CreateSnapshot();
            Assert.Equal(0, calls);
            Assert.Equal(0, processor.DisposeCount);
            Assert.Same(processor, app.ServiceProvider.GetRequiredService<IImageProcessor>());
            app.InvalidateContent();
            Assert.Same(processor, app.ServiceProvider.GetRequiredService<IImageProcessor>());
            Assert.Equal(1, calls);
        }
        Assert.Equal(1, processor.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomProcessor_PublishesReusesOutput_AndHonorsOwnership(bool callerOwned)
    {
        Directory.CreateDirectory(Path.Combine(_root, "content"));
        File.WriteAllText(Path.Combine(_root, "content", "post.md"), "---\ntitle: Image\n---\n![custom](./source.png)");
        File.WriteAllText(Path.Combine(_root, "content", "source.png"), "custom processor input");
        var processor = new TrackingImageProcessor();
        await using (var app = CreateApp())
        {
            app.UseServices(services =>
            {
                if (callerOwned) { services.AddSingleton<IImageProcessor>(processor); }
                else { services.AddSingleton<IImageProcessor>(_ => processor); }
            });
            app.UseMarkdownContent<FrontMatter>();
            app.AddPages<MarkdownPostTestPage>(provider => provider.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
                .Select(post => new { Slug = Path.GetFileNameWithoutExtension(post.Value.FileInfo.Name), ContentKey = post.Key }));
            var output = Path.Combine(_root, "dist");
            await app.PublishAsync(output);
            Assert.Equal(1, processor.Calls);
            var html = File.ReadAllText(Path.Combine(output, "md", "post", "index.html"));
            Assert.Contains("custom.svg", html, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output, "md", "post", "custom.svg")));
            await app.PublishAsync(output);
            Assert.Equal(1, processor.Calls);
            Assert.True(File.Exists(Path.Combine(output, "md", "post", "custom.svg")));
            Assert.Equal(0, processor.DisposeCount);
        }
        Assert.Equal(callerOwned ? 0 : 1, processor.DisposeCount);
        if (callerOwned) { await processor.DisposeAsync(); }
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task NonSingletonProcessor_FailsBeforeResolution(ServiceLifetime lifetime)
    {
        await using var invalid = CreateApp();
        invalid.UseServices(services => services.Add(new ServiceDescriptor(typeof(IImageProcessor),
            _ => throw new InvalidOperationException("Factory must not run."), lifetime)));
        var exception = Assert.Throws<InvalidOperationException>(() => invalid.CreateSnapshot());
        Assert.Contains(nameof(IImageProcessor), exception.Message, StringComparison.Ordinal);
        Assert.Contains("Singleton", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingUnkeyedProcessor_FailsBeforeRendering(bool keyedOnly)
    {
        await using var invalid = CreateApp();
        invalid.UseServices(services =>
        {
            services.RemoveAll<IImageProcessor>();
            if (keyedOnly) { services.AddKeyedSingleton<IImageProcessor, TrackingImageProcessor>("other"); }
        });
        var exception = Assert.Throws<InvalidOperationException>(() => invalid.CreateSnapshot());
        Assert.Contains(nameof(IImageProcessor), exception.Message, StringComparison.Ordinal);
        Assert.Contains("unkeyed", exception.Message, StringComparison.Ordinal);
    }

    private StaticSite CreateApp()
    {
        var app = StaticSite.Create([], new SiteExecutionPaths(_root));
        app.Info = new SiteInfo { Name = "Images", BaseUrl = new Uri("https://example.test/") };
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
