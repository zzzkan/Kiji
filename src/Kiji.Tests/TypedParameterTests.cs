using Microsoft.Extensions.DependencyInjection;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Kiji.Hosting;
using Kiji.Routing;
using Kiji.Tests.TestSite.TypedParameters;
using Xunit;

namespace Kiji.Tests;

public sealed class TypedParameterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-typed-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", 7)]
    [InlineData("  ", 0)]
    [InlineData("A/B", 1)]
    public async Task Render_PreservesTypesNullsAndReferences(string? summary, int? count)
    {
        var log = new ParameterRenderLog();
        var model = new ParameterModel("model title");
        string[] tags = ["dotnet", "kiji"];
        var builder = CreateBuilder("render", log);
        builder.AddPageParameters<TypedParameterPage>(_ => [new { Id = 42, Featured = true, Kind = DayOfWeek.Friday, Count = count, Summary = summary, Tags = tags, Model = model, Resource = model }]);
        await using (var site = builder.Build())
        {
            var page = Assert.Single(site.CreateSnapshot().Pages);
            Assert.Equal("/typed/42/", page.RoutePath);
            Assert.Equal(42, Assert.IsType<int>(page.Parameters["Id"]));
            var html = await site.RenderPageAsync(page, CancellationToken.None);
            Assert.Contains($"42|True|Friday|{count}|{summary}|dotnet,kiji|model title", html, StringComparison.Ordinal);
            var rendered = log.Pages[42];
            Assert.Same(model, rendered.Model);
            Assert.Same(model, rendered.Resource);
            Assert.Same(tags, rendered.Tags);
            Assert.Equal(count, rendered.Count);
            Assert.Equal(summary, rendered.Summary);
        }
        Assert.False(model.IsDisposed);
        model.Dispose();
    }

    [Fact]
    public async Task DictionaryInputs_AreCopiedAndReadOnlyDictionariesAreRecognized()
    {
        var builder = CreateBuilder("dictionaries", new ParameterRenderLog());
        var mutable = new Dictionary<string, object?> { ["Id"] = 1 };
        builder.AddPageParameters<TypedParameterPage>(_ =>
        [
            mutable,
            new Dictionary<string, object?> { ["Id"] = 2 }.ToFrozenDictionary(),
            new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?> { ["Id"] = 3 }),
        ]);
        await using var site = builder.Build();
        var pages = site.CreateSnapshot().Pages;
        mutable["Id"] = 99;
        Assert.Equal(1, pages[0].Parameters["Id"]);
        Assert.Equal(["/typed/1/", "/typed/2/", "/typed/3/"], pages.Select(page => page.RoutePath));
        foreach (var page in pages) { await site.RenderPageAsync(page, CancellationToken.None); }
    }

    [Fact]
    public async Task DictionaryKeysDifferingOnlyByCase_AreRejectedDuringNormalization()
    {
        var builder = CreateBuilder("duplicate-keys", new ParameterRenderLog());
        builder.AddPageParameters<TypedParameterPage>(_ =>
            [new Dictionary<string, object?>(StringComparer.Ordinal) { ["Id"] = 1, ["id"] = 2 }]);

        await using var site = builder.Build();
        Assert.Throws<ArgumentException>(() => site.CreateSnapshot());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(7)]
    public async Task ParameterNames_AreCaseInsensitiveDuringPlanningAndRendering(int? count)
    {
        var log = new ParameterRenderLog();
        var builder = CreateBuilder("parameter-casing", log);
        builder.AddPageParameters<TypedParameterPage>(_ => [new { id = 42, COUNT = count, sUMMARY = "mixed case" }]);

        await using var site = builder.Build();
        var page = Assert.Single(site.CreateSnapshot().Pages);
        Assert.Equal("/typed/42/", page.RoutePath);
        await site.RenderPageAsync(page, CancellationToken.None);
        Assert.Equal(42, log.Pages[42].Id);
        Assert.Equal(count, log.Pages[42].Count);
        Assert.Equal("mixed case", log.Pages[42].Summary);
    }

    [Theory]
    [InlineData("Id", "42", "System.Int32", "System.String")]
    [InlineData("Id", null, "System.Int32", "null")]
    [InlineData("Count", 1L, "System.Nullable", "System.Int64")]
    [InlineData("COUNT", 1L, "System.Nullable", "System.Int64")]
    [InlineData("Summary", 42, "System.String", "System.Int32")]
    [InlineData("Kind", "Friday", "System.DayOfWeek", "System.String")]
    public async Task IncompatibleValue_FailsDuringPlanning(string name, object? value, string expected, string actual)
    {
        var builder = CreateBuilder("invalid", new ParameterRenderLog());
        var values = new Dictionary<string, object?> { ["Id"] = 42, [name] = value };
        builder.AddPageParameters<TypedParameterPage>(_ => [values]);
        await using var site = builder.Build();
        var error = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains(typeof(TypedParameterPage).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains("/typed/{Id}/", error.Message, StringComparison.Ordinal);
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Contains(actual, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputGetter_IsReadOncePerSnapshot()
    {
        var builder = CreateBuilder("getter", new ParameterRenderLog());
        var input = new ParameterInput();
        builder.AddPageParameters<TypedParameterPage>(_ => [input]);
        await using var site = builder.Build();
        var page = Assert.Single(site.CreateSnapshot().Pages);
        await site.RenderPageAsync(page, CancellationToken.None);
        await site.RenderPageAsync(page, CancellationToken.None);
        Assert.Equal(1, input.GetReadCount());
    }

    [Fact]
    public async Task PrivateSetter_IsRejectedAndMetadataIsClearedOnHotReload()
    {
        var builder = CreateBuilder("setter", new ParameterRenderLog());
        builder.AddPageParameters<ReadOnlyParameterPage>(_ => [new { Id = 1, Value = "text" }]);
        await using var site = builder.Build();
        var error = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("public setter", error.Message, StringComparison.Ordinal);
        var before = PageDiscovery.Parameters(typeof(ReadOnlyParameterPage));
        HotReloadHandler.ClearCache([typeof(ReadOnlyParameterPage)]);
        var after = PageDiscovery.Parameters(typeof(ReadOnlyParameterPage));
        Assert.NotSame(before, after);
        Assert.Equal(typeof(int), after["Id"].PropertyType);
    }

    [Fact]
    public async Task ComplexParameters_RenderEveryBuildWithoutInvalidatingScalarPages()
    {
        var log = new ParameterRenderLog();
        var model = new ParameterModel("before");
        var siteRoot = Path.Combine(_root, "incremental");
        async Task Build(string directory, ParameterRenderLog observer)
        {
            var builder = CreateBuilder(directory, observer);
            builder.AddPageParameters<TypedParameterPage>(_ => [new { Id = 1, Model = model }, new { Id = 2, Summary = "stable" }]);
            await using var site = builder.Build();
            await site.PublishAsync("dist");
        }
        await Build("incremental", log);
        var output = Path.Combine(siteRoot, "dist", "typed", "1", "index.html");
        var stamp = File.GetLastWriteTimeUtc(output);
        await Build("incremental", log);
        Assert.Equal(2, log.Counts[1]);
        Assert.Equal(1, log.Counts[2]);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(output));
        model.Title = "after";
        await Build("incremental", log);
        Assert.Equal(3, log.Counts[1]);
        Assert.Equal(1, log.Counts[2]);
        await Build("scratch", new ParameterRenderLog());
        foreach (var file in Directory.EnumerateFiles(Path.Combine(siteRoot, "dist"), "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Path.Combine(siteRoot, "dist"), file);
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(_root, "scratch", "dist", relative)), await File.ReadAllBytesAsync(file));
        }
        var manifestPath = Path.Combine(siteRoot, ".kiji", "cache", "manifest.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        Assert.Null(json["Pages"]!.AsArray().Single(page => page!["RoutePath"]!.GetValue<string>() == "/typed/1/")!["ParametersHash"]);
        json["SchemaVersion"] = 2;
        await File.WriteAllTextAsync(manifestPath, json.ToJsonString());
        await Build("incremental", log);
        Assert.Equal(2, log.Counts[2]);
    }

    [Fact]
    public async Task DevServer_AndPublishRenderTheSameTypedValues()
    {
        var publishing = CreateBuilder("publish", new ParameterRenderLog());
        publishing.AddPageParameters<TypedParameterPage>(_ => [new { Id = 42, Count = (int?)7, Featured = true }]);
        await using var published = publishing.Build();
        await published.PublishAsync("dist");
        var html = await File.ReadAllTextAsync(Path.Combine(_root, "publish", "dist", "typed", "42", "index.html"));
        var serving = CreateBuilder("serve", new ParameterRenderLog());
        serving.AddPageParameters<TypedParameterPage>(_ => [new { Id = 42, Count = (int?)7, Featured = true }]);
        await using var served = serving.Build();
        var (server, web) = await served.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None);
        await using (server)
        {
            using var client = new HttpClient();
            var response = await client.GetStringAsync(new Uri(new Uri(web.Urls.First()), "/typed/42/"));
            Assert.Equal(html, response.Replace("<script src=\"/_kiji/livereload.js\" defer></script>", "", StringComparison.Ordinal));
        }
    }

    private StaticSiteBuilder CreateBuilder(string directory, ParameterRenderLog log)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(Path.Combine(_root, directory)));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        Directory.CreateDirectory(Path.Combine(builder.Paths.ProjectDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(builder.Paths.ProjectDirectory, "wwwroot"));
        builder.Services.AddSingleton(log);
        return builder;
    }
}
