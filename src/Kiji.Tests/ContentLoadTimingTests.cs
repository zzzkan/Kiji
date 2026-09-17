using System.Reflection;
using Kiji.Markdown;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

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

    private StaticSite CreateApp()
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.Paths.RootDirectory = _testDir;
        app.Paths.ContentDirectory = "contents";
        app.UseMarkdownContent<FrontMatter>(key: static post => post.FileInfo.FullName);
        return app;
    }

    [Fact]
    public void StaticSite_ExposesNoPublicRouteToContentOrServices()
    {
        var reachable = typeof(StaticSite)
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
        Assert.Empty(typeof(MarkdownContent<>).GetConstructors());
    }

    [Fact]
    public void ResolvingPathsBeforeACommand_Throws()
    {
        var app = CreateApp();

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.ServiceProvider.GetRequiredService<ResolvedSitePaths>());

        Assert.Contains("before a command settled", exception.Message, StringComparison.Ordinal);
    }

}
