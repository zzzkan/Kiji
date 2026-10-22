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

        Assert.Contains("boundary-publish-warning", output, StringComparison.Ordinal);
        Assert.DoesNotContain("boundary-publish-information", output, StringComparison.Ordinal);
        Assert.Contains("Boundary fallback", Read("boundary/index.html"), StringComparison.Ordinal);

        var html = Read("index.html");
        Assert.Contains("<title>First article</title>", html, StringComparison.Ordinal);
        Assert.Contains("Original body.", html, StringComparison.Ordinal);
        Assert.Matches("<p b-[a-z0-9]+>Hello</p>", html);
        Assert.Contains("color: teal", Read("PackageSite.styles.css"), StringComparison.Ordinal);
        var rclCss = Regex.Match(Read("PackageSite.styles.css"), "@import '([^']+)'").Groups[1].Value;
        Assert.Contains("color: purple", Read(rclCss), StringComparison.Ordinal);
        Assert.Contains("export const library = true", File.ReadAllText(PublicFile(AssetUrl(html, "library"))), StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(fixture.Site, "dist"), "*.webp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Site, "dist"), "*.dll", SearchOption.AllDirectories));

        var oldUrl = AssetUrl(html);
        fixture.Replace("Site/content/article.md", "Original body.", "Edited body.");
        fixture.Replace("Site/wwwroot/app.js", "original", "modified");
        fixture.Replace("Library/Card.resx", "Hello", "Welcome");
        await fixture.PublishAsync();
        html = Read("index.html");
        Assert.Contains("Edited body.", html, StringComparison.Ordinal);
        Assert.Matches("<p b-[a-z0-9]+>Welcome</p>", html);
        Assert.NotEqual(oldUrl, AssetUrl(html));
        Assert.False(File.Exists(PublicFile(oldUrl)));
        Assert.Contains("modified", File.ReadAllText(PublicFile(AssetUrl(html))), StringComparison.Ordinal);

        // This file is deliberately untracked: the MSBuild switch must reach Kiji.
        Assert.Contains("external-value-1", html, StringComparison.Ordinal);
        fixture.Write("Site/value.txt", "external-value-2");
        await fixture.PublishAsync("-p:KijiForce=true");
        Assert.Contains("external-value-2", Read("index.html"), StringComparison.Ordinal);

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

        // MSBuild paths can contain backslashes on Unix; use Kiji's normalized path.
        var stagingDirectory = (await fixture.DotnetAsync("msbuild", "Site/PackageSite.csproj", "-p:Configuration=Release",
            "-t:KijiResolveStagingDir", "-getProperty:_KijiStagingDir")).Trim();
        Assert.True(Directory.Exists(stagingDirectory), $"Missing staging directory: {stagingDirectory}");
        await fixture.DotnetAsync("clean", "Site/PackageSite.csproj", "-c", "Release");
        Assert.False(Directory.Exists(Path.Combine(fixture.Site, ".kiji")));
        Assert.False(Directory.Exists(stagingDirectory));
    });

    [Theory]
    [InlineData("Microsoft.NET.Sdk.Razor")]
    [InlineData("Microsoft.NET.Sdk.Web")]
    public Task Watch_RazorAndAssets_Reload(string sdk) => fixture.ScenarioAsync(async () =>
    {
        await fixture.WatchAsync(async client =>
        {
            var html = await client.GetStringAsync("/kiji/");
            Assert.Contains("/kiji/_kiji/livereload.js", html, StringComparison.Ordinal);
            Assert.DoesNotContain("aspnetcore-browser-refresh.js", html, StringComparison.Ordinal);
            await ReloadAsync(client, () => fixture.Replace("Site/wwwroot/app.js", "original", "modified"));
            Assert.Contains("modified", await client.GetStringAsync(AssetUrl(html)), StringComparison.Ordinal);
            await ReloadAsync(client, () => fixture.Replace("Site/Home.razor", "Package consumer", "Updated Razor"));
            Assert.Contains("Updated Razor", await client.GetStringAsync("/kiji/"), StringComparison.Ordinal);
            await ReloadAsync(client, () => fixture.Replace("Site/Home.razor.css", "teal", "magenta"));
            Assert.Contains("magenta", await client.GetStringAsync(AssetUrl(html, "scoped")), StringComparison.Ordinal);
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
        var duplicate = socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.NotSame(duplicate, await Task.WhenAny(duplicate, Task.Delay(1000, timeout.Token)));
        socket.Abort();
        try { await duplicate; }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }
}
