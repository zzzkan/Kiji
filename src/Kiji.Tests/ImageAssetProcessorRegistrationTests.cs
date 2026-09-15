using Kiji.Assets;
using Kiji.Markdown;
using Kiji.Tests.TestSite.PageServices;
using Microsoft.Extensions.DependencyInjection;
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
            app.UseImageAssetProcessor(() => throw new InvalidOperationException("Superseded factory must not run."));
            app.UseImageAssetProcessor(() => { calls++; return processor; });
            app.CreateSnapshot();
            Assert.Equal(0, calls);
            Assert.Equal(0, processor.DisposeCount);
            Assert.Same(processor, app.ServiceProvider.GetRequiredService<IImageAssetProcessor>());
            app.InvalidateContent();
            Assert.Same(processor, app.ServiceProvider.GetRequiredService<IImageAssetProcessor>());
            Assert.Equal(1, calls);
        }
        Assert.Equal(1, processor.DisposeCount);
    }

    [Fact]
    public async Task CustomProcessor_IsUsedByMarkdownPublish_AndItsOutputIsPreserved()
    {
        Directory.CreateDirectory(Path.Combine(_root, "contents"));
        File.WriteAllText(Path.Combine(_root, "contents", "post.md"), "---\ntitle: Image\n---\n![custom](./source.png)");
        File.WriteAllText(Path.Combine(_root, "contents", "source.png"), "custom processor input");
        var processor = new TrackingImageProcessor();
        await using var app = CreateApp();
        app.UseImageAssetProcessor(() => processor);
        app.UseMarkdownContent<FrontMatter>(post => post.FileInfo.Slug);
        app.AddPages<MarkdownPostTestPage>(provider => provider.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(post => new { Slug = post.Key, ContentKey = post.Key }));
        var output = Path.Combine(_root, "dist");
        await app.PublishAsync(output);
        Assert.Equal(1, processor.Calls);
        var html = File.ReadAllText(Path.Combine(output, "md", "post", "index.html"));
        Assert.Contains("custom.svg", html, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output, "md", "post", "custom.svg")));
        await app.PublishAsync(output);
        Assert.Equal(1, processor.Calls);
        Assert.True(File.Exists(Path.Combine(output, "md", "post", "custom.svg")));
    }

    [Fact]
    public async Task NullFactoryResult_HasUsefulDiagnostic()
    {
        await using var invalid = CreateApp();
        invalid.UseImageAssetProcessor(() => null!);
        var exception = Assert.Throws<InvalidOperationException>(() => invalid.ServiceProvider.GetRequiredService<IImageAssetProcessor>());
        Assert.Contains("factory returned null", exception.Message, StringComparison.Ordinal);
    }

    private StaticSite CreateApp()
    {
        var app = StaticSite.Create([]);
        app.Info = new SiteInfo { Name = "Images", BaseUrl = new Uri("https://example.test/") };
        app.Paths.RootDirectory = _root;
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
