using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kiji.Tests.TestSite;
using Xunit;

namespace Kiji.Tests;

public sealed class PackagedStaticAssetTests(PackagedSiteFixture fixture) : IClassFixture<PackagedSiteFixture>
{
    [Fact]
    public async Task EmptySite_PublishesAndStartsWithoutSdkAssets()
    {
        var site = fixture.CreateSite(nameof(EmptySite_PublishesAndStartsWithoutSdkAssets));
        await fixture.PublishAsync(site);
        Assert.Equal(["index.html"], Directory.GetFiles(Path.Combine(site, "dist")).Select(Path.GetFileName));
        await PackagedSiteFixture.WithServerAsync(site, async client =>
            Assert.Contains("SDK regression", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_ResolvesScopedAndRclAssetsAndReplacesEditedFingerprint()
    {
        var site = fixture.CreateSite(nameof(Publish_ResolvesScopedAndRclAssetsAndReplacesEditedFingerprint), assets: true);
        var library = Path.Combine(site, "..", "library");
        PackagedSiteFixture.Write(library, "Library.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Razor\"><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /></ItemGroup></Project>");
        PackagedSiteFixture.Write(library, "Card.razor", "<p>Library card</p>");
        PackagedSiteFixture.Write(library, "Card.razor.css", "p { color: purple; }");
        PackagedSiteFixture.Write(library, "wwwroot/library.js", "export const library = true;");
        var project = Path.Combine(site, "Site.csproj");
        File.WriteAllText(project, File.ReadAllText(project).Replace("</Project>", "<ItemGroup><ProjectReference Include=\"../library/Library.csproj\" /></ItemGroup></Project>", StringComparison.Ordinal));
        File.AppendAllText(Path.Combine(site, "Home.razor"), "\n<Library.Card />\n<a id=\"library\" href=\"@Assets[\"_content/Library/library.js\"]\">library</a>");
        await fixture.PublishAsync(site);
        var html = File.ReadAllText(Path.Combine(site, "dist/index.html"));
        var oldUrl = AssetUrl(html);
        var css = File.ReadAllText(Path.Combine(site, "dist/AssetSite.styles.css"));
        Assert.Contains("color: teal", css, StringComparison.Ordinal);
        var libraryCss = Regex.Match(css, "@import '([^']+)'").Groups[1].Value;
        Assert.StartsWith("_content/Library/", libraryCss, StringComparison.Ordinal);
        Assert.Contains("color: purple", File.ReadAllText(Path.Combine(site, "dist", libraryCss)), StringComparison.Ordinal);
        Assert.Matches("<h1 b-[a-z0-9]+>", html);
        Assert.Equal("export const library = true;", File.ReadAllText(PublicFile(site, AssetUrl(html, "library"))));

        var source = Path.Combine(site, "wwwroot/app.js");
        File.WriteAllText(source, File.ReadAllText(source).Replace("original", "modified", StringComparison.Ordinal));
        await fixture.PublishAsync(site);
        var newUrl = AssetUrl(File.ReadAllText(Path.Combine(site, "dist/index.html")));
        Assert.NotEqual(oldUrl, newUrl);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(PublicFile(site, newUrl)));
        Assert.False(File.Exists(PublicFile(site, oldUrl)));
    }

    [Fact]
    public async Task Build_SolutionCompilesSharedLibrariesOnce()
    {
        var site = fixture.CreateSite(Path.Combine(nameof(Build_SolutionCompilesSharedLibrariesOnce), "site"), assets: true);
        var root = Path.GetDirectoryName(site)!;
        const string recordCompile = """
            <Target Name="RecordCompileVisit" BeforeTargets="CoreCompile">
              <MakeDir Directories="$(IntermediateOutputPath)compile-visits" />
              <WriteLinesToFile File="$(IntermediateOutputPath)compile-visits/$([System.Guid]::NewGuid()).txt" Lines="$(MSBuildProjectFullPath)" />
            </Target>
            """;
        foreach (var (name, sdk) in new[] { ("Helper", "Microsoft.NET.Sdk"), ("Library", "Microsoft.NET.Sdk.Razor") })
        {
            var library = Path.Combine(root, name);
            PackagedSiteFixture.Write(library, $"{name}.csproj", $"<Project Sdk=\"{sdk}\"><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /></ItemGroup>{recordCompile}</Project>");
            PackagedSiteFixture.Write(library, "Value.cs", $"namespace {name}; public static class Value {{ public static string Text => \"Shared library\"; }}");
        }
        PackagedSiteFixture.Write(Path.Combine(root, "Library"), "wwwroot/library.js", "export const library = true;");
        var project = Path.Combine(site, "Site.csproj");
        File.WriteAllText(project, File.ReadAllText(project).Replace("</Project>", "<ItemGroup><ProjectReference Include=\"../Helper/Helper.csproj\" /><ProjectReference Include=\"../Library/Library.csproj\" /></ItemGroup></Project>", StringComparison.Ordinal));
        PackagedSiteFixture.Write(root, "Assets.slnx", """
            <Solution>
              <Project Path="site/Site.csproj" />
              <Project Path="Helper/Helper.csproj" />
              <Project Path="Library/Library.csproj" />
            </Solution>
            """);

        // A property added only on the site's references creates a second build
        // of each shared library, even when a file-sharing failure happens not to occur.
        await fixture.DotnetAsync(root, "build", "Assets.slnx", "-c", "Release", "-m:4");
        foreach (var name in new[] { "Helper", "Library" })
        {
            Assert.Single(Directory.GetFiles(Path.Combine(root, name, "obj/Release/net10.0/compile-visits"), "*.txt"));
        }
    }

    [Fact]
    public async Task Publish_UnchangedSiteDoesNotInvokeCompilerOrAppHost()
    {
        var site = fixture.CreateSite(nameof(Publish_UnchangedSiteDoesNotInvokeCompilerOrAppHost), assets: true);
        var first = await fixture.PublishAsync(site, "-v:diag");
        Assert.Contains("Task \"Csc\"", first, StringComparison.Ordinal);
        var log = await fixture.PublishAsync(site, "-v:diag");
        Assert.DoesNotContain("Task \"Csc\"", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Task \"CreateAppHost\"", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_WorkerCompressionAndManifestStayCorrectAcrossOptionChanges()
    {
        var site = fixture.CreateSite(nameof(Publish_WorkerCompressionAndManifestStayCorrectAcrossOptionChanges), assets: true, worker: true);
        foreach (var compressed in new[] { true, false, true })
        {
            await fixture.PublishAsync(site, $"-p:CompressionEnabled={compressed.ToString().ToLowerInvariant()}");
            var worker = File.ReadAllText(Path.Combine(site, "dist/service-worker.js"));
            Assert.Contains("published worker", worker, StringComparison.Ordinal);
            Assert.Contains("Manifest version", worker, StringComparison.OrdinalIgnoreCase);
            var manifestText = File.ReadAllText(Path.Combine(site, "dist/service-worker-assets.js"));
            using var manifest = JsonDocument.Parse(manifestText[manifestText.IndexOf('{')..].Trim().TrimEnd(';'));
            var page = Assert.Single(manifest.RootElement.GetProperty("assets").EnumerateArray(), asset => asset.GetProperty("url").GetString() == "index.html");
            Assert.Equal("sha256-" + Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(Path.Combine(site, "dist/index.html")))), page.GetProperty("hash").GetString());
            if (!compressed)
            {
                Assert.DoesNotContain(Directory.GetFiles(Path.Combine(site, "dist"), "*", SearchOption.AllDirectories), path => path.EndsWith(".gz", StringComparison.Ordinal) || path.EndsWith(".br", StringComparison.Ordinal));
                continue;
            }
            foreach (var file in new[] { "index.html", "service-worker.js" })
            {
                foreach (var extension in new[] { ".gz", ".br" })
                {
                    using var input = File.OpenRead(Path.Combine(site, "dist", file + extension));
                    using Stream decoder = extension == ".gz" ? new GZipStream(input, CompressionMode.Decompress) : new BrotliStream(input, CompressionMode.Decompress);
                    using var decoded = new MemoryStream();
                    await decoder.CopyToAsync(decoded);
                    Assert.Equal(File.ReadAllBytes(Path.Combine(site, "dist", file)), decoded.ToArray());
                }
            }
        }
    }

    [Fact]
    public async Task Serve_FingerprintSupportsHttpAndLiveChangesWithoutRebuild()
    {
        var site = fixture.CreateSite(nameof(Serve_FingerprintSupportsHttpAndLiveChangesWithoutRebuild), assets: true);
        await fixture.PublishAsync(site, "-p:CompressionEnabled=true");
        await PackagedSiteFixture.WithServerAsync(site, async client =>
        {
            var html = await client.GetStringAsync("/kiji/");
            var url = AssetUrl(html);
            Assert.DoesNotContain("\"integrity\"", html, StringComparison.Ordinal);
            using var first = await client.GetAsync(url);
            var original = await first.Content.ReadAsByteArrayAsync();
            Assert.Equal(File.ReadAllBytes(Path.Combine(site, "wwwroot/app.js")), original);
            Assert.Equal(original, await GetGzipAsync(client, url));
            Assert.NotNull(first.Headers.ETag);
            using var conditional = new HttpRequestMessage(HttpMethod.Get, url);
            conditional.Headers.IfNoneMatch.Add(first.Headers.ETag);
            using var unchanged = await client.SendAsync(conditional);
            Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
            using var range = new HttpRequestMessage(HttpMethod.Get, url);
            range.Headers.Range = new RangeHeaderValue(0, 9);
            using var partial = await client.SendAsync(range);
            Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
            Assert.Equal(original[..10], await partial.Content.ReadAsByteArrayAsync());

            await ChangeLiveFileAsync(() => PackagedSiteFixture.Write(site, "wwwroot/app.js", "export const changed = true;"));
            Assert.Equal("export const changed = true;", await client.GetStringAsync(url));
            Assert.Equal(File.ReadAllBytes(Path.Combine(site, "wwwroot/app.js")), await GetGzipAsync(client, url));
            PackagedSiteFixture.Write(site, "wwwroot/new.js", "new asset");
            Assert.Equal("new asset", await client.GetStringAsync("/kiji/new.js"));
            await ChangeLiveFileAsync(() => File.Delete(Path.Combine(site, "wwwroot/app.js")));
            using var removed = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
            using var outsidePrefix = await client.GetAsync("/new.js");
            Assert.Equal(HttpStatusCode.NotFound, outsidePrefix.StatusCode);
        });
    }

    private static string AssetUrl(string html, string id = "asset") =>
        WebUtility.HtmlDecode(Regex.Match(html, $"id=\"{id}\" href=\"([^\"]+)\"").Groups[1].Value);

    private static async Task<byte[]> GetGzipAsync(HttpClient client, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        await gzip.CopyToAsync(decoded);
        return decoded.ToArray();
    }

    private static async Task ChangeLiveFileAsync(Action change)
    {
        // Receiving the last response byte can precede disposal of the server's
        // file stream. Only retry Windows sharing violations, never stale output.
        for (var attempt = 0; ; attempt++)
        {
            try { change(); return; }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33 && attempt < 40)
            {
                await Task.Delay(25);
            }
        }
    }

    private static string PublicFile(string site, string url)
    {
        Assert.StartsWith("/kiji/", url, StringComparison.Ordinal);
        return Path.Combine(site, "dist", Uri.UnescapeDataString(url["/kiji/".Length..]));
    }
}
