using Kiji.Assets;
using Kiji.Markdown;
using Kiji.Tests.TestSite.PageServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kiji.Tests;

public sealed class ImageAssetProcessorRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-image-service-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomProcessor_PublishesReusesOutput_AndHonorsOwnership(bool callerOwned)
    {
        Directory.CreateDirectory(Path.Combine(_root, "content"));
        File.WriteAllText(Path.Combine(_root, "content", "post.md"), "---\ntitle: Image\n---\n![custom](./source.png)");
        File.WriteAllText(Path.Combine(_root, "content", "source.png"), "custom processor input");
        var processor = new TrackingImageProcessor();
        var builder = CreateBuilder();
        builder.Services.AddTransient<IImageProcessor>(_ => throw new InvalidOperationException("Superseded factory must not run."));
        if (callerOwned) { builder.Services.AddSingleton<IImageProcessor>(processor); }
        else { builder.Services.AddSingleton<IImageProcessor>(_ => processor); }
        builder.Services.AddKeyedScoped<IImageProcessor>("other", (_, _) => throw new InvalidOperationException("Keyed factory must not run."));
        builder.AddMarkdownContent<FrontMatter>();
        builder.AddPageParameters<MarkdownPostTestPage>(provider => provider.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(post => new { Slug = Path.GetFileNameWithoutExtension(post.Value.FileInfo.Name), ContentKey = post.Key }));
        await using (var app = builder.Build())
        {
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
    public void NonSingletonProcessor_FailsBeforeResolution(ServiceLifetime lifetime)
    {
        var invalid = CreateBuilder();
        invalid.Services.Add(new ServiceDescriptor(typeof(IImageProcessor),
            _ => throw new InvalidOperationException("Factory must not run."), lifetime));
        var exception = Assert.Throws<InvalidOperationException>(() => invalid.Build());
        Assert.Contains(nameof(IImageProcessor), exception.Message, StringComparison.Ordinal);
        Assert.Contains("Singleton", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void KeyedProcessor_CannotReplaceTheUnkeyedProcessor()
    {
        var invalid = CreateBuilder();
        invalid.Services.RemoveAll<IImageProcessor>();
        invalid.Services.AddKeyedSingleton<IImageProcessor, TrackingImageProcessor>("other");
        var exception = Assert.Throws<InvalidOperationException>(() => invalid.Build());
        Assert.Contains(nameof(IImageProcessor), exception.Message, StringComparison.Ordinal);
        Assert.Contains("unkeyed", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("quality", "Quality")]
    [InlineData("max-width", "MaxWidth")]
    [InlineData("widths", "Widths")]
    public void InvalidSettings_FailBuildBeforeAnyOutput(string setting, string property)
    {
        var builder = CreateBuilder();
        builder.Services.Configure<ImageProcessorOptions>(options =>
        {
            switch (setting)
            {
                case "quality": options.Quality = 101; break;
                case "max-width": options.MaxWidth = 0; break;
                case "widths": options.Widths = [640, 0]; break;
            }
        });

        var exception = Assert.Throws<OptionsValidationException>(() => builder.Build());

        Assert.Contains($"ImageProcessorOptions.{property}", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_root));
    }

    private StaticSiteBuilder CreateBuilder()
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_root));
        builder.Info = new SiteInfo { Name = "Images", BaseUrl = new Uri("https://example.test/") };
        return builder;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
