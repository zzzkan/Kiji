using System.IO.Compression;
using System.Net;
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
    public Task Publish_Edit_AndClean() => fixture.ScenarioAsync(async () =>
    {
        fixture.Write("Site/wwwroot/selected.txt", "All-kind fallback");
        fixture.Write("Site/wwwroot/publish-variant.txt", "Publish-kind replacement");
        fixture.Write("Site/Directory.Build.targets", """
            <Project>
              <Target Name="ConfigurePublishVariant" AfterTargets="ResolvePublishStaticWebAssets">
                <ItemGroup>
                  <StaticWebAsset Condition="'%(Filename)%(Extension)' == 'selected.txt'" RelativePath="selected.txt" />
                  <StaticWebAsset Condition="'%(Filename)%(Extension)' == 'publish-variant.txt'"
                                  RelativePath="selected.txt" AssetKind="Publish" CopyToPublishDirectory="Never" />
                </ItemGroup>
              </Target>
            </Project>
            """);
        fixture.Write("Site/BoundaryPage.razor", """
            @page "/boundary/"
            <ErrorBoundary>
                <ChildContent><FailingChild /></ChildContent>
                <ErrorContent><p>Boundary fallback</p></ErrorContent>
            </ErrorBoundary>
            """);
        fixture.Write("Site/FailingChild.razor", """
            @using Microsoft.Extensions.Logging
            @inject ILogger<FailingChild> Logger
            @code {
                protected override void OnInitialized()
                {
                    Logger.LogInformation("boundary-publish-information");
                    throw new InvalidOperationException("boundary-publish-warning");
                }
            }
            """);

        var output = await fixture.PublishAsync();

        Assert.Contains("Unhandled exception rendering component: boundary-publish-warning", output, StringComparison.Ordinal);
        Assert.DoesNotContain("boundary-publish-information", output, StringComparison.Ordinal);
        // Logs are high-importance messages, which a redirected publish shows by default.
        Assert.Matches(@"Generated \d+ pages \(\d+ unchanged, skipped", output);
        Assert.DoesNotContain("Unchanged: ", output, StringComparison.Ordinal);
        Assert.Contains("Boundary fallback", Read("boundary/index.html"), StringComparison.Ordinal);

        var html = Read("index.html");
        Assert.Contains("<title>First article</title>", html, StringComparison.Ordinal);
        Assert.Contains("Original body.", html, StringComparison.Ordinal);
        Assert.Matches("<p b-[a-z0-9]+>Hello</p>", html);
        Assert.Contains("color: teal", Read("PackageSite.styles.css"), StringComparison.Ordinal);
        Assert.Contains("Linked asset original.", Read("nested/linked.txt"), StringComparison.Ordinal);
        // Never on the selected publish variant must not expose the All-kind fallback.
        Assert.False(File.Exists(Path.Combine(fixture.Site, "dist", "selected.txt")));
        var rclCss = Regex.Match(Read("PackageSite.styles.css"), "@import '([^']+)'").Groups[1].Value;
        Assert.Contains("color: purple", Read(rclCss), StringComparison.Ordinal);
        Assert.Contains("export const library = true", File.ReadAllText(PublicFile(AssetUrl(html, "library"))), StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(fixture.Site, "dist"), "*.webp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Site, "dist"), "*.dll", SearchOption.AllDirectories));
        // By default, neither the site nor a referenced library may publish compressed copies.
        // The SDK compresses both unless Kiji's default is applied.
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(fixture.Site, "dist"), "*", SearchOption.AllDirectories),
            path => Path.GetExtension(path) is ".gz" or ".br");

        var oldUrl = AssetUrl(html);
        fixture.Replace("Site/content/article.md", "Original body.", "Edited body.");
        fixture.Replace("Site/wwwroot/app.js", "original", "modified");
        fixture.Replace("Site/assets/linked.txt", "original", "modified");
        fixture.Replace("Library/Card.resx", "Hello", "Welcome");
        // Opt in before the next HTML change, so the compressed copy must be refreshed.
        await fixture.PublishAsync("-p:CompressionEnabled=true");
        html = Read("index.html");
        Assert.Contains("Edited body.", html, StringComparison.Ordinal);
        Assert.Contains("Linked asset modified.", Read("nested/linked.txt"), StringComparison.Ordinal);
        Assert.Matches("<p b-[a-z0-9]+>Welcome</p>", html);
        Assert.NotEqual(oldUrl, AssetUrl(html));
        Assert.False(File.Exists(PublicFile(oldUrl)));
        Assert.Contains("modified", File.ReadAllText(PublicFile(AssetUrl(html))), StringComparison.Ordinal);

        // Run must serve after publish even when its environment contains publish paths.
        await using (var server = RunningCommand.Start(Path.GetDirectoryName(fixture.Site)!,
            "run", "--project", "Site/PackageSite.csproj", "-c", "Release", "--no-build",
            "-e", "KIJI_OUTPUT=inherited-output", "-e", "Kiji.OutputPath=inherited-output",
            "--", "--urls", "http://127.0.0.1:0"))
        {
            using var client = new HttpClient { BaseAddress = await server.AddressAsync() };
            Assert.Contains("Edited body.", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal);
        }

        // SDK post-processing must include Kiji's generated HTML.
        Assert.Contains("published worker", Read("service-worker.js"), StringComparison.Ordinal);
        var manifestText = Read("service-worker-assets.js");
        using var manifest = JsonDocument.Parse(manifestText[manifestText.IndexOf('{')..].Trim().TrimEnd(';'));
        var page = Assert.Single(manifest.RootElement.GetProperty("assets").EnumerateArray(), asset => asset.GetProperty("url").GetString() == "index.html");
        Assert.Equal("sha256-" + Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Site, "dist/index.html")))), page.GetProperty("hash").GetString());
        using (var input = File.OpenRead(Path.Combine(fixture.Site, "dist/index.html.gz")))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
        { Assert.Equal(Read("index.html"), await reader.ReadToEndAsync()); }
        Assert.True(File.Exists(PublicFile(AssetUrl(Read("index.html"), "library")) + ".gz"));

        // An exception fails the publish through the exit code, and the output says why.
        fixture.Write("Site/UnregisteredPage.razor", """
            @page "/unregistered/{Slug}/"
            @code { [Parameter] public string Slug { get; set; } = ""; }
            """);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.PublishAsync());
        Assert.Matches(@"InvalidOperationException: Page component '[^']*UnregisteredPage'.*AddPageParameters<UnregisteredPage>", failure.Message);
        File.Delete(Path.Combine(fixture.Site, "UnregisteredPage.razor"));

        // A worker outside wwwroot keeps its project path, which must never resolve to its source.
        fixture.Replace("Site/PackageSite.csproj", "wwwroot/service-worker.js", "worker/service-worker.js");
        Directory.CreateDirectory(Path.Combine(fixture.Site, "worker"));
        File.Move(Path.Combine(fixture.Site, "wwwroot", "service-worker.js"), Path.Combine(fixture.Site, "worker", "service-worker.js"));

        // Logs never fail a publish, even when a message reads like an MSBuild error:
        // its HTML is cached, and a later publish would reuse it without the message.
        fixture.Write("Site/LoggingPage.razor", """
            @page "/logging/"
            @using Microsoft.Extensions.Logging
            @inject ILogger<LoggingPage> Logger
            <p>Log-only result</p>
            @code { protected override void OnInitialized() => Logger.LogError("error: logging-page-error"); }
            """);
        Assert.Contains("error: logging-page-error", await fixture.PublishAsync("-warnaserror"), StringComparison.Ordinal);
        Assert.Contains("Log-only result", Read("logging/index.html"), StringComparison.Ordinal);
        Assert.Contains("published worker", Read("worker/service-worker.js"), StringComparison.Ordinal);
        Assert.DoesNotContain("published worker", File.ReadAllText(Path.Combine(fixture.Site, "worker", "service-worker.js")), StringComparison.Ordinal);

        var stagingDirectory = Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Site, "obj"), "site", SearchOption.AllDirectories));
        // This file is deliberately untracked. Cleaning must make the next publish render it again.
        Assert.Contains("external-value-1", Read("index.html"), StringComparison.Ordinal);
        fixture.Write("Site/value.txt", "external-value-2");
        await fixture.DotnetAsync("clean", "Site/PackageSite.csproj", "-c", "Release");
        Assert.False(Directory.Exists(Path.Combine(fixture.Site, ".kiji")));
        Assert.False(Directory.Exists(stagingDirectory));
        await fixture.PublishAsync();
        Assert.Contains("external-value-2", Read("index.html"), StringComparison.Ordinal);
    });

    [Theory]
    [InlineData("Microsoft.NET.Sdk.Razor")]
    [InlineData("Microsoft.NET.Sdk.Web")]
    public Task Watch_ReloadsChanges_AndShowsCompilationErrors(string sdk) => fixture.ScenarioAsync(async () =>
    {
        await fixture.WatchAsync(async client =>
        {
            var html = await client.GetStringAsync("/kiji/");
            Assert.Contains("/kiji/_kiji/livereload.js", html, StringComparison.Ordinal);
            Assert.DoesNotContain("aspnetcore-browser-refresh.js", html, StringComparison.Ordinal);
            Assert.Contains("Linked asset original.", await client.GetStringAsync("/kiji/nested/linked.txt"), StringComparison.Ordinal);
            await ReloadAsync(client, () => fixture.Replace("Site/content/article.md", "Original body.", "Edited body."));
            Assert.Contains("Edited body.", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal);
            Assert.Contains("Edited body.", await client.GetStringAsync("/kiji/articles/article/"), StringComparison.Ordinal);
            await ReloadAsync(client, () => fixture.Write("Site/content/posts/added.md", """
                ---
                title: Added article
                ---
                Added body.
                """));
            Assert.Contains("Added article", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal);
            Assert.Contains("Added body.", await client.GetStringAsync("/kiji/articles/added/"), StringComparison.Ordinal);
            await ReloadAsync(client, () => File.Delete(Path.Combine(fixture.Site, "content", "posts", "added.md")));
            Assert.DoesNotContain("Added article", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal);
            using var removed = await client.GetAsync("/kiji/articles/added/");
            Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
            await ReloadAsync(client, () => fixture.Replace("Site/wwwroot/app.js", "original", "modified"));
            Assert.Contains("modified", await client.GetStringAsync(AssetUrl(html)), StringComparison.Ordinal);
            await ReloadAsync(client, () => fixture.Replace("Site/Home.razor", "Package consumer", "Updated Razor"));
            Assert.Contains("Updated Razor", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal);
            await ReloadAsync(client, () => fixture.Replace("Site/Home.razor.css", "teal", "magenta"));
            Assert.Contains("magenta", await client.GetStringAsync(AssetUrl(html, "scoped")), StringComparison.Ordinal);

            // dotnet watch reports compilation errors only through its private browser
            // refresh server, which Razor SDK sites get from Kiji's targets. Kiji relays
            // them to open pages and pages opened later.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var socket = await ConnectReloadAsync(client, timeout.Token);
            await EditAsync(() => fixture.Replace("Site/Home.razor", "Updated Razor", "@MissingMember"), timeout.Token);
            Assert.Contains("MissingMember", await ReceiveAsync(socket, timeout.Token), StringComparison.Ordinal);
            using (var opened = await ConnectReloadAsync(client, timeout.Token))
            {
                Assert.Contains("MissingMember", await ReceiveAsync(opened, timeout.Token), StringComparison.Ordinal);
            }
            // Diagnostics contain local source paths. A foreign page, opaque origin,
            // or rebinding domain must not subscribe even while errors are retained.
            await AssertReloadRejectedAsync(client, "https://attacker.example");
            await AssertReloadRejectedAsync(client, "null");
            await AssertReloadRejectedAsync(client, "http://attacker.example", "attacker.example");
            await EditAsync(() => fixture.Replace("Site/Home.razor", "@MissingMember", "Fixed Razor"), timeout.Token);
            bool cleared = false, reloaded = false;
            while (!cleared || !reloaded)
            {
                var message = await ReceiveAsync(socket, timeout.Token);
                if (message == "reload") { Assert.False(reloaded, "Fixing the error reloaded twice."); reloaded = true; }
                else { cleared = message == "[]"; }
            }
            Assert.Contains("Fixed Razor", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal);
        });
    }, sdk);

    [Fact]
    public async Task Cleanup_WaitsForFileLockRelease()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows prevents deletion while a file is open without delete sharing.");
        FileStream? heldFile = null;
        var cleanup = fixture.ScenarioAsync(() =>
        {
            heldFile = File.Open(Path.Combine(fixture.Site, "Home.razor"), FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.CompletedTask;
        });
        try
        {
            Assert.NotNull(heldFile);
            Assert.False(cleanup.IsCompleted, "Cleanup must wait for the file lock to be released.");
        }
        finally
        {
            heldFile?.Dispose();
            await cleanup;
        }
        Assert.False(Directory.Exists(fixture.Site));
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(fixture.Site, "dist", relative));
    private string PublicFile(string url) => Path.Combine(fixture.Site, "dist", Uri.UnescapeDataString(url["/kiji/".Length..]));
    private static string AssetUrl(string html, string id = "asset") => WebUtility.HtmlDecode(Regex.Match(html, $"id=\"{id}\" href=\"([^\"]+)\"").Groups[1].Value);

    private static async Task ReloadAsync(HttpClient client, Action edit)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var socket = await ConnectReloadAsync(client, timeout.Token);
        await EditAsync(edit, timeout.Token);
        var buffer = new byte[64];
        var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, received.Count));
        var duplicate = socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.NotSame(duplicate, await Task.WhenAny(duplicate, Task.Delay(1000, timeout.Token)));
        socket.Abort();
        try { await duplicate; }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private static async Task<ClientWebSocket> ConnectReloadAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        await socket.ConnectAsync(new UriBuilder(client.BaseAddress!) { Scheme = "ws", Path = "/kiji/_kiji/reload" }.Uri, cancellationToken);
        return socket;
    }

    private static async Task AssertReloadRejectedAsync(HttpClient client, string origin, string? host = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/kiji/_kiji/reload");
        request.Headers.Connection.Add("Upgrade");
        request.Headers.Upgrade.ParseAdd("websocket");
        request.Headers.Add("Sec-WebSocket-Version", "13");
        request.Headers.Add("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==");
        request.Headers.Add("Origin", origin);
        request.Headers.Host = host;
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(host is null ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task EditAsync(Action edit, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { edit(); return; }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33 && attempt < 40) { await Task.Delay(25, cancellationToken); }
        }
    }

    private static async Task<string> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            message.Write(buffer, 0, received.Count);
        } while (!received.EndOfMessage);
        return Encoding.UTF8.GetString(message.ToArray());
    }
}
