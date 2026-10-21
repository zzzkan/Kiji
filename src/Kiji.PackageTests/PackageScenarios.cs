using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Kiji.PackageTests;

public sealed class PackageScenarios(PackageFixture fixture) : IClassFixture<PackageFixture>
{
    [Fact]
    public Task Publish_Edit_AndRepublish() => fixture.ScenarioAsync(nameof(Publish_Edit_AndRepublish), async () =>
    {
        var first = await fixture.PublishAsync("-v:diag");
        Assert.True(first.Compiled, first.Output);
        var html = Read("index.html");
        Assert.Contains("<title>First article</title>", html, StringComparison.Ordinal);
        Assert.Matches("<p b-[a-z0-9]+>Hello</p>", html);
        Assert.Contains("Original body.", html, StringComparison.Ordinal);
        Assert.Matches("<h1 b-[a-z0-9]+>", html);
        Assert.Contains("color: teal", Read("PackageSite.styles.css"), StringComparison.Ordinal);
        var rclCss = Regex.Match(Read("PackageSite.styles.css"), "@import '([^']+)'").Groups[1].Value;
        Assert.Contains("color: purple", Read(rclCss), StringComparison.Ordinal);
        Assert.Equal(File.ReadAllText(Path.Combine(fixture.Work, "Library/wwwroot/library.js")), File.ReadAllText(PublicFile(AssetUrl(html, "library"))));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(fixture.Site, "dist"), "*.webp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Site, "dist"), "*.dll", SearchOption.AllDirectories));
        await fixture.AssertPackageAndLinksAsync();
        var unchanged = await fixture.PublishAsync("-v:diag");
        Assert.False(unchanged.Compiled, unchanged.Output);
        Assert.Equal(html, Read("index.html"));

        // A real Razor consumer checks attribute propagation and the MSBuild force switch.
        for (var round = 2; round <= 4; round++)
        {
            if (round == 2)
            {
                fixture.Remove("Site/dist");
                fixture.Remove("Site/obj/Release/net10.0/kiji/site");
            }
            fixture.Write("Site/value.txt", $"external-value-{round}");
            await fixture.PublishAsync(round == 3 ? ["-p:KijiForce=true"] : []);
            Assert.Contains(round < 3 ? "external-value-1" : "external-value-3", Read("cached/index.html"), StringComparison.Ordinal);
            foreach (var page in new[] { "always", "nested", "layout" })
            { Assert.Contains($"external-value-{round}", Read($"{page}/index.html"), StringComparison.Ordinal); }
        }

        var oldUrl = AssetUrl(Read("index.html"));
        fixture.Replace("Site/content/article.md", "Original body.", "Edited body.");
        fixture.Replace("Site/wwwroot/app.js", "original", "modified");
        fixture.Replace("Library/Card.resx", "Hello", "Welcome");
        await fixture.PublishAsync();
        html = Read("index.html");
        Assert.Contains("Edited body.", html, StringComparison.Ordinal);
        Assert.Matches("<p b-[a-z0-9]+>Welcome</p>", html);
        Assert.NotEqual(oldUrl, AssetUrl(html));
        Assert.False(File.Exists(PublicFile(oldUrl)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(fixture.Site, "wwwroot/app.js")), File.ReadAllBytes(PublicFile(AssetUrl(html))));

        // Inspect the edited publish, then exercise both compression transitions.
        await AssertPublishedAssetsAsync(compressed: true);
        foreach (var compressed in new[] { false, true })
        {
            await fixture.PublishAsync(compressed ? [] : ["-p:CompressionEnabled=false"]);
            await AssertPublishedAssetsAsync(compressed);
        }
    });

    [Fact]
    public Task Build_OutsideProject_AndClean() => fixture.ScenarioAsync(nameof(Build_OutsideProject_AndClean), async () =>
    {
        fixture.Replace("Site/PackageSite.csproj", "<OutputType>Exe</OutputType>", "<OutputType>Exe</OutputType><BaseOutputPath>../build-output/</BaseOutputPath>");
        await fixture.DotnetAsync("build", "Consumer.slnx", "-c", "Release", "-m:4");
        foreach (var library in new[] { "Library", "Helper" })
        { Assert.Single(File.ReadAllLines(Path.Combine(fixture.Work, library, "obj/Release/net10.0/compile-visits.txt"))); }
        await fixture.DotnetAsync("build", "Site/PackageSite.csproj", "-c", "Debug");
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Site, "obj"), "*.gz", SearchOption.AllDirectories));
        await fixture.PublishAsync("--no-build");
        Assert.True(File.Exists(Path.Combine(fixture.Site, "dist/index.html.gz")));
        await fixture.AssertPackageAndLinksAsync();
        var expected = Read("index.html");
        await fixture.DotnetAsync("publish", "Site/PackageSite.csproj", "-c", "Release", "--no-build", "-o", "other-output");
        Assert.Equal(expected, File.ReadAllText(Path.Combine(fixture.Work, "other-output/index.html")));
        await fixture.WithServerAsync(false, async client =>
            Assert.Contains("First article", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal), "build-output/Release/net10.0/PackageSite.dll");
        await fixture.WithServerAsync(true, async client =>
            Assert.Contains("First article", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(fixture.Site, ".kiji/dev-site")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Work, ".kiji")));
        await fixture.DotnetAsync("clean", "Site/PackageSite.csproj", "-c", "Release");
        Assert.False(Directory.Exists(Path.Combine(fixture.Site, ".kiji")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Site, "obj/Release/net10.0/kiji/site")));

        // Reuse the same site path for the SDK's special empty-assets case.
        fixture.Remove("Site");
        fixture.Remove("build-output");
        fixture.Write("Site/PackageSite.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Razor\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><PackageReference Include=\"Kiji\" Version=\"$(KijiPackageVersion)\" /></ItemGroup></Project>");
        fixture.Write("Site/Program.cs", "using Kiji; var site = StaticSite.Create(args); site.Info = new() { Name = \"Empty\", BaseUrl = new Uri(\"https://example.test/kiji/\") }; site.AddPages(); await site.RunAsync();");
        fixture.Write("Site/Home.razor", "@page \"/\"\n<h1>Empty consumer</h1>");
        await fixture.PublishAsync();
        Assert.Equal(["index.html", "index.html.br", "index.html.gz"], Directory.GetFiles(Path.Combine(fixture.Site, "dist")).Select(Path.GetFileName).Order());
        await fixture.WithServerAsync(false, async client => Assert.Contains("Empty consumer", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal));
    });

    [Theory]
    [InlineData("Microsoft.NET.Sdk.Razor", false)]
    [InlineData("Microsoft.NET.Sdk.Web", true)]
    public Task Develop_Edit_AndReload(string sdk, bool compressed) => fixture.ScenarioAsync($"{nameof(Develop_Edit_AndReload)}: {sdk}", async () =>
    {
        fixture.Replace("Site/PackageSite.csproj", "<OutputType>Exe</OutputType>", $"<OutputType>Exe</OutputType><DisableBuildCompression>{(!compressed).ToString().ToLowerInvariant()}</DisableBuildCompression>");
        foreach (var path in new[] { "wwwroot/app.js", "Home.razor.js" })
        { File.SetLastWriteTimeUtc(Path.Combine(fixture.Site, path), DateTime.UtcNow.AddMinutes(-1)); }
        var log = await fixture.WithServerAsync(true, async client =>
        {
            var html = await client.GetStringAsync("/kiji/");
            Assert.Contains("/kiji/_kiji/livereload.js", html, StringComparison.Ordinal);
            Assert.DoesNotContain("aspnetcore-browser-refresh.js", html, StringComparison.Ordinal);
            foreach (var (id, path) in new[] { ("asset", "wwwroot/app.js"), ("collocated", "Home.razor.js") })
            {
                var url = AssetUrl(html, id);
                using var first = await client.GetAsync(url);
                var original = await first.Content.ReadAsStringAsync();
                var tag = first.Headers.ETag;
                var date = first.Content.Headers.LastModified;
                Assert.NotNull(tag);
                Assert.NotNull(date);
                using var range = new HttpRequestMessage(HttpMethod.Get, url);
                range.Headers.Range = new RangeHeaderValue(0, 9);
                using var partial = await client.SendAsync(range);
                Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
                Assert.Equal(original[..10], await partial.Content.ReadAsStringAsync());
                for (var edit = 0; edit < 2; edit++)
                {
                    var changed = original + $"\n/* edit {edit} */";
                    await ReloadAsync(client, () =>
                    {
                        fixture.Write("Site/" + path, changed);
                        File.SetLastWriteTimeUtc(Path.Combine(fixture.Site, path), DateTime.UtcNow.AddSeconds(-10 + edit));
                    });
                    foreach (var useDate in new[] { false, true })
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, url);
                        if (useDate) { request.Headers.IfModifiedSince = date; }
                        else { request.Headers.IfNoneMatch.Add(tag); }
                        using var response = await client.SendAsync(request);
                        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                        Assert.Equal(changed, await response.Content.ReadAsStringAsync());
                    }
                    using var current = await client.GetAsync(url);
                    tag = current.Headers.ETag!;
                    date = current.Content.Headers.LastModified;
                    using var conditional = new HttpRequestMessage(HttpMethod.Head, url);
                    conditional.Headers.IfNoneMatch.Add(tag);
                    using var unchanged = await client.SendAsync(conditional);
                    Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
                    using var gzipRequest = new HttpRequestMessage(HttpMethod.Get, url);
                    gzipRequest.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
                    using var gzipResponse = await client.SendAsync(gzipRequest);
                    if (compressed)
                    {
                        Assert.Contains("gzip", gzipResponse.Content.Headers.ContentEncoding);
                        using var stream = await gzipResponse.Content.ReadAsStreamAsync();
                        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
                        using var reader = new StreamReader(gzip);
                        Assert.Equal(changed, await reader.ReadToEndAsync());
                    }
                    else { Assert.Empty(gzipResponse.Content.Headers.ContentEncoding); }
                }
            }
            await ReloadAsync(client, () => fixture.Replace("Site/Home.razor", "Package consumer", "Updated Razor"), checkSingle: true);
            Assert.Contains("Updated Razor", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal);
            await ReloadAsync(client, () => fixture.Replace("Site/Home.razor.css", "teal", "magenta"), checkSingle: true);
            Assert.Contains("magenta", await client.GetStringAsync(AssetUrl(html, "scoped")), StringComparison.Ordinal);
            await ReloadAsync(client, () => File.Delete(Path.Combine(fixture.Site, "wwwroot/app.js")));
            using var removed = await client.GetAsync(AssetUrl(html));
            Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        });
        Assert.Contains("Page cache refreshed after a code update", log, StringComparison.Ordinal);
        Assert.DoesNotContain(log.Split('\n'), line => line.Contains("Static asset", StringComparison.Ordinal)
            && (line.TrimEnd().EndsWith("Home.razor", StringComparison.Ordinal) || line.TrimEnd().EndsWith("Home.razor.css", StringComparison.Ordinal)));
        await fixture.AssertPackageAndLinksAsync();
    }, sdk);

    private string Read(string relative) => File.ReadAllText(Path.Combine(fixture.Site, "dist", relative));
    private string PublicFile(string url) => Path.Combine(fixture.Site, "dist", Uri.UnescapeDataString(url["/kiji/".Length..]));
    private static string AssetUrl(string html, string id = "asset") => WebUtility.HtmlDecode(Regex.Match(html, $"id=\"{id}\" href=\"([^\"]+)\"").Groups[1].Value);

    private async Task AssertPublishedAssetsAsync(bool compressed)
    {
        Assert.Contains("published worker", Read("service-worker.js"), StringComparison.Ordinal);
        var manifestText = Read("service-worker-assets.js");
        using var manifest = JsonDocument.Parse(manifestText[manifestText.IndexOf('{')..].Trim().TrimEnd(';'));
        var page = Assert.Single(manifest.RootElement.GetProperty("assets").EnumerateArray(), asset => asset.GetProperty("url").GetString() == "index.html");
        Assert.Equal("sha256-" + Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Site, "dist/index.html")))), page.GetProperty("hash").GetString());
        foreach (var file in new[] { "index.html", "service-worker.js", "app.js" })
        {
            foreach (var extension in new[] { ".gz", ".br" })
            {
                var path = Path.Combine(fixture.Site, "dist", file);
                Assert.Equal(compressed, File.Exists(path + extension));
                if (!compressed) { continue; }
                using var input = File.OpenRead(path + extension);
                using Stream decoder = extension == ".gz" ? new GZipStream(input, CompressionMode.Decompress) : new BrotliStream(input, CompressionMode.Decompress);
                using var decoded = new MemoryStream();
                await decoder.CopyToAsync(decoded);
                Assert.Equal(File.ReadAllBytes(path), decoded.ToArray());
            }
        }
    }

    private static async Task ReloadAsync(HttpClient client, Action edit, bool checkSingle = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new UriBuilder(client.BaseAddress!) { Scheme = "ws", Path = "/kiji/_kiji/reload" }.Uri, timeout.Token);
        for (var attempt = 0; ; attempt++)
        {
            try { edit(); break; }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33 && attempt < 40) { await Task.Delay(25, timeout.Token); }
        }
        var buffer = new byte[64];
        var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, received.Count));
        if (checkSingle)
        {
            var duplicate = socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            Assert.NotSame(duplicate, await Task.WhenAny(duplicate, Task.Delay(1000, timeout.Token)));
            socket.Abort();
            try { await duplicate; }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }
        else { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token); }
    }
}
