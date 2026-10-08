using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
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
        Assert.Equal(["index.html", "index.html.br", "index.html.gz"], Directory.GetFiles(Path.Combine(site, "dist")).Select(Path.GetFileName).Order());
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
    public async Task Build_SkipsCompressionByDefaultAndAllowsExplicitOptIn()
    {
        var site = fixture.CreateSite(nameof(Build_SkipsCompressionByDefaultAndAllowsExplicitOptIn), assets: true);
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            await fixture.DotnetAsync(site, "build", "-c", configuration);
            Assert.DoesNotContain(Directory.GetFiles(Path.Combine(site, "obj", configuration), "*", SearchOption.AllDirectories),
                path => path.EndsWith(".gz", StringComparison.Ordinal) || path.EndsWith(".br", StringComparison.Ordinal));
        }

        // Publish must still compress when it reuses a build without compressed assets.
        await fixture.PublishAsync(site, "--no-build");
        Assert.True(File.Exists(Path.Combine(site, "dist/app.js.gz")));
        Assert.True(File.Exists(Path.Combine(site, "dist/app.js.br")));

        await fixture.DotnetAsync(site, "build", "-p:DisableBuildCompression=false");
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(site, "obj/Debug"), "*.gz", SearchOption.AllDirectories));
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
            await fixture.PublishAsync(site, compressed ? [] : ["-p:CompressionEnabled=false"]);
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
            var asset = Path.GetRelativePath(Path.Combine(site, "dist"), PublicFile(site, AssetUrl(File.ReadAllText(Path.Combine(site, "dist/index.html")))));
            foreach (var file in new[] { "index.html", "service-worker.js", "app.js", asset })
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Serve_FingerprintSupportsHttpAndLiveChangesWithoutRebuild(bool buildCompression)
    {
        var site = fixture.CreateSite($"{nameof(Serve_FingerprintSupportsHttpAndLiveChangesWithoutRebuild)}-{buildCompression}", assets: true);
        await fixture.PublishAsync(site, buildCompression ? ["-p:DisableBuildCompression=false"] : []);
        await PackagedSiteFixture.WithServerAsync(site, async client =>
        {
            var html = await client.GetStringAsync("/kiji/");
            var url = AssetUrl(html);
            Assert.DoesNotContain("\"integrity\"", html, StringComparison.Ordinal);
            using var first = await client.GetAsync(url);
            var original = await first.Content.ReadAsByteArrayAsync();
            Assert.Equal(File.ReadAllBytes(Path.Combine(site, "wwwroot/app.js")), original);
            Assert.Equal(original, await GetWithGzipRequestAsync(client, url, buildCompression));
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
            using var revalidate = new HttpRequestMessage(HttpMethod.Get, url);
            revalidate.Headers.IfNoneMatch.Add(first.Headers.ETag);
            using var edited = await client.SendAsync(revalidate);
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            Assert.Equal("export const changed = true;", await edited.Content.ReadAsStringAsync());
            Assert.Equal("export const changed = true;", await client.GetStringAsync(url));
            Assert.Equal(File.ReadAllBytes(Path.Combine(site, "wwwroot/app.js")), await GetWithGzipRequestAsync(client, url, buildCompression));
            PackagedSiteFixture.Write(site, "wwwroot/new.js", "new asset");
            Assert.Equal("new asset", await client.GetStringAsync("/kiji/new.js"));
            await ChangeLiveFileAsync(() => File.Delete(Path.Combine(site, "wwwroot/app.js")));
            using var removed = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
            using var outsidePrefix = await client.GetAsync("/new.js");
            Assert.Equal(HttpStatusCode.NotFound, outsidePrefix.StatusCode);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Watch_AssetEditsReloadAndRevalidateBrowserCache(bool buildCompression)
    {
        var site = fixture.CreateSite($"{nameof(Watch_AssetEditsReloadAndRevalidateBrowserCache)}-{buildCompression}", assets: true);
        PackagedSiteFixture.Write(site, "wwwroot/site.css", "h1 { color: teal; }");
        PackagedSiteFixture.Write(site, "Home.razor.js", "export const value = 'original';");
        var assets = new[] { ("asset", "wwwroot/app.js"), ("css", "wwwroot/site.css"), ("collocated", "Home.razor.js") };
        foreach (var (_, path) in assets)
        {
            File.SetLastWriteTimeUtc(Path.Combine(site, path), DateTime.UtcNow.AddMinutes(-1));
        }
        File.AppendAllText(Path.Combine(site, "Home.razor"), """

            <a id="css" href="@Assets["site.css"]">css</a>
            <a id="collocated" href="@Assets["Home.razor.js"]">js</a>
            """);
        var project = Path.Combine(site, "Site.csproj");
        File.WriteAllText(project, File.ReadAllText(project).Replace("</Project>",
            $"<PropertyGroup><DisableBuildCompression>{(!buildCompression).ToString().ToLowerInvariant()}</DisableBuildCompression></PropertyGroup></Project>", StringComparison.Ordinal));

        await PackagedSiteFixture.WithServerAsync(site, async client =>
        {
            var html = await client.GetStringAsync("/kiji/");
            Assert.Contains("/kiji/_kiji/livereload.js", html, StringComparison.Ordinal);
            foreach (var (id, path) in assets)
            {
                var url = AssetUrl(html, id);
                using var cached = await client.GetAsync(url);
                cached.EnsureSuccessStatusCode();
                var original = await cached.Content.ReadAsStringAsync();
                Assert.NotNull(cached.Headers.ETag);
                Assert.NotNull(cached.Content.Headers.LastModified);
                var previousETag = cached.Headers.ETag;
                var previousDate = cached.Content.Headers.LastModified;
                for (var edit = 0; edit < 2; edit++)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var socket = new ClientWebSocket();
                    await socket.ConnectAsync(new UriBuilder(client.BaseAddress!) { Scheme = "ws", Path = "/kiji/_kiji/reload" }.Uri, timeout.Token);
                    var expected = original + $"\n/* edit {edit} */";
                    await ChangeLiveFileAsync(() =>
                    {
                        PackagedSiteFixture.Write(site, path, expected);
                        // HTTP dates have second precision. Ensure a distinct validator
                        // even on fast machines and keep the second edit the same size.
                        File.SetLastWriteTimeUtc(Path.Combine(site, path), DateTime.UtcNow.AddSeconds(-10 + edit));
                    });
                    var buffer = new byte[64];
                    var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                    Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, received.Count));
                    EntityTagHeaderValue? currentETag = null;
                    DateTimeOffset? currentDate = null;
                    for (var validator = 0; validator < 2; validator++)
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, url);
                        if (validator == 0) { request.Headers.IfNoneMatch.Add(previousETag); }
                        else { request.Headers.IfModifiedSince = previousDate; }
                        using var response = await client.SendAsync(request);
                        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                        Assert.Equal(expected, await response.Content.ReadAsStringAsync());
                        currentETag = response.Headers.ETag;
                        currentDate = response.Content.Headers.LastModified;
                        Assert.NotNull(currentETag);
                        Assert.NotNull(currentDate);
                        using var unchanged = new HttpRequestMessage(HttpMethod.Head, url);
                        unchanged.Headers.IfNoneMatch.Add(currentETag);
                        using var validated = await client.SendAsync(unchanged);
                        Assert.Equal(HttpStatusCode.NotModified, validated.StatusCode);
                    }
                    previousETag = currentETag!;
                    previousDate = currentDate;
                    Assert.Equal(Encoding.UTF8.GetBytes(expected), await GetWithGzipRequestAsync(client, url, buildCompression));
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
                }
            }
        }, watch: true);
    }

    [Theory]
    [InlineData("Microsoft.NET.Sdk.Razor")]
    [InlineData("Microsoft.NET.Sdk.Web")]
    public async Task Watch_KijiOwnsRefreshAfterRazorAndScopedCssUpdates(string sdk)
    {
        var site = fixture.CreateSite($"{nameof(Watch_KijiOwnsRefreshAfterRazorAndScopedCssUpdates)}-{sdk}", assets: true);
        // Watch derives the scoped CSS bundle name from the project filename.
        // Keep it aligned with AssemblyName, as in an ordinary SDK project.
        var project = Path.Combine(site, "Site.csproj");
        File.WriteAllText(project, File.ReadAllText(project).Replace("Microsoft.NET.Sdk.Razor", sdk, StringComparison.Ordinal));
        File.Move(project, Path.Combine(site, "AssetSite.csproj"));
        // Collocated JS places the project root in ContentRoots, reproducing the
        // accidental source-file notifications from Razor and scoped CSS edits.
        PackagedSiteFixture.Write(site, "Home.razor.js", "export const value = 1;");
        // Include a closing body tag so watch could inject its client if the
        // host accidentally started enabling browser-refresh middleware.
        PackagedSiteFixture.Write(site, "Home.razor", """
            @page "/"
            <html><head><title>Watch regression</title></head><body>
            <h1>SDK regression</h1>
            <link id="scoped" href="@Assets["AssetSite.styles.css"]" rel="stylesheet" />
            </body></html>
            """);
        // Allow watch's browser-refresh machinery while preventing an actual
        // browser launch. The slim Kiji host must still inject only its own client.
        PackagedSiteFixture.Write(site, "Properties/launchSettings.json", """
            { "profiles": { "Site": { "commandName": "Project", "launchBrowser": false } } }
            """);
        await PackagedSiteFixture.WithServerAsync(site, async client =>
        {
            var html = await client.GetStringAsync("/kiji/");
            Assert.Contains("/kiji/_kiji/livereload.js", html, StringComparison.Ordinal);
            Assert.DoesNotContain("aspnetcore-browser-refresh.js", html, StringComparison.Ordinal);
            var cssUrl = AssetUrl(html, "scoped");
            for (var edit = 0; edit < 2; edit++)
            {
                var code = edit == 0;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new UriBuilder(client.BaseAddress!) { Scheme = "ws", Path = "/kiji/_kiji/reload" }.Uri, timeout.Token);
                if (code)
                {
                    var path = Path.Combine(site, "Home.razor");
                    await File.WriteAllTextAsync(path, File.ReadAllText(path).Replace("SDK regression", "Updated Razor", StringComparison.Ordinal), timeout.Token);
                }
                else
                {
                    await File.WriteAllTextAsync(Path.Combine(site, "Home.razor.css"), "h1 { color: magenta; }", timeout.Token);
                }
                var buffer = new byte[64];
                var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, received.Count));
                Assert.Contains("Updated Razor", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal);
                if (!code) { Assert.Contains("magenta", await client.GetStringAsync(cssUrl), StringComparison.Ordinal); }

                // Keep the same client connected long enough to catch a second
                // notification from the source watcher or generated asset watcher.
                var duplicate = socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                Assert.NotSame(duplicate, await Task.WhenAny(duplicate, Task.Delay(1000, timeout.Token)));
                socket.Abort();
                try { await duplicate; }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
            }
        }, watch: true, browserRefresh: true);
        var log = File.ReadAllText(Path.Combine(site, "server.log"));
        Assert.Contains("Page cache refreshed after a code update", log, StringComparison.Ordinal);
        Assert.Contains("Static asset changed:", log, StringComparison.Ordinal);
        Assert.DoesNotContain(log.Split('\n'), line => line.Contains("Static asset", StringComparison.Ordinal)
            && (line.TrimEnd().EndsWith("Home.razor", StringComparison.Ordinal) || line.TrimEnd().EndsWith("Home.razor.css", StringComparison.Ordinal)));
    }

    private static string AssetUrl(string html, string id = "asset") =>
        WebUtility.HtmlDecode(Regex.Match(html, $"id=\"{id}\" href=\"([^\"]+)\"").Groups[1].Value);

    private static async Task<byte[]> GetWithGzipRequestAsync(HttpClient client, string url, bool compressed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        if (!compressed)
        {
            Assert.Empty(response.Content.Headers.ContentEncoding);
            return await response.Content.ReadAsByteArrayAsync();
        }
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
