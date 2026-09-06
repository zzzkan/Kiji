using System.Reflection;
using Kiji.Markdown;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Loading content resolves <see cref="SsgOptions"/>, and those are a singleton decided
/// by the command being run — so whatever resolves them first fixes the output paths for
/// the whole process. These pin down that no public API can reach content, or a service
/// provider, while the site is still being declared.
/// </summary>
public sealed class ContentLoadTimingTests : IDisposable
{
    private readonly string _testDir;

    public ContentLoadTimingTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ContentLoadTimingTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_testDir, "contents"));
        File.WriteAllText(
            Path.Combine(_testDir, "contents", "hello.md"),
            "---\ntitle: Hello\ncreatedAt: 2026-03-18\n---\n\nBody.\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private KijiApp CreateApp()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = _testDir;
        builder.Paths.Content = "contents";
        builder.AddMarkdownContent<FrontMatter>(key: static post => post.FileInfo.Slug);
        return builder.Build();
    }

    /// <summary>
    /// The structural guarantee: a site declaring routes has no way to get hold of a
    /// content dictionary, nor of the provider that would hand it one. Content is only
    /// reachable from the services passed to a route factory, a feed factory, a content
    /// loader, or a site artifact — all of which run after a command settles the paths.
    /// </summary>
    [Fact]
    public void KijiApp_ExposesNoPublicRouteToContentOrServices()
    {
        var reachable = typeof(KijiApp)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(static member => member switch
            {
                PropertyInfo property => (Name: property.Name, Type: property.PropertyType),
                MethodInfo method => (Name: method.Name, Type: method.ReturnType),
                _ => (Name: member.Name, Type: typeof(void)),
            })
            .Where(static member =>
                member.Type == typeof(IServiceProvider) ||
                (member.Type.IsGenericType && member.Type.GetGenericTypeDefinition() == typeof(ContentDictionary<>)))
            .Select(static member => member.Name)
            .ToArray();

        Assert.Empty(reachable);
    }

    /// <summary>
    /// The invariant behind that guarantee, asserted directly: nothing may resolve the
    /// site's paths before a command decides them. No public API can reach this, which is
    /// the point — the guard exists so a future default cannot silently reintroduce it.
    /// </summary>
    [Fact]
    public void ResolvingPathsBeforeACommand_Throws()
    {
        var app = CreateApp();

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.Services.GetRequiredService<SsgOptions>());

        Assert.Contains("before a command settled", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The failure the whole arrangement exists to prevent: dev serves from its own
    /// mirror, and would have written into the build output directory had anything
    /// resolved the paths earlier.
    /// </summary>
    [Fact]
    public async Task DevServer_ResolvesItsOwnOutputMirror_NotTheBuildDirectory()
    {
        await using var app = CreateApp();

        var (devServer, web) = await app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None);
        await using (devServer)
        {
            var options = app.Services.GetRequiredService<SsgOptions>();
            Assert.Contains(".kiji", options.OutputPath, StringComparison.OrdinalIgnoreCase);
            await web.StopAsync();
        }
    }
}
