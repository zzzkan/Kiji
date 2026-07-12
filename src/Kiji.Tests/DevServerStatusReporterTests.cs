using Kiji.Hosting;
using Xunit;

namespace Kiji.Tests;

public sealed class DevServerStatusReporterTests
{
    private static readonly string Esc = ((char)0x1b).ToString();

    [Fact]
    public void DevServerStarted_WithAnsiColor_FormatsLeaderLikeWatch()
    {
        using var output = new StringWriter();
        var reporter = new DevServerStatusReporter(output, prefix: "kiji dev", useEmoji: true, useAnsiColor: true);

        reporter.DevServerStarted(new Uri("http://127.0.0.1:8080/"), @"C:\site\contents", @"C:\site\wwwroot");

        var log = output.ToString();
        Assert.Contains($"{Esc}[90mkiji dev    🚀{Esc}[0m Started Kiji dev server at http://127.0.0.1:8080/", log, StringComparison.Ordinal);
        Assert.Contains($"{Esc}[90mkiji dev    ⌚{Esc}[0m Watching content files under 'C:\\site\\contents'.", log, StringComparison.Ordinal);
    }

    [Fact]
    public void WatcherError_WithAnsiColor_WritesYellowMessage()
    {
        using var output = new StringWriter();
        var reporter = new DevServerStatusReporter(output, prefix: "kiji dev", useEmoji: true, useAnsiColor: true);

        reporter.WatcherError(WatchedPathSource.Content, @"C:\site\contents", new IOException("boom"));

        var log = output.ToString();
        Assert.Contains($"{Esc}[90mkiji dev    ⚠{Esc}[0m {Esc}[33mFile watcher for content path 'C:\\site\\contents' failed: boom{Esc}[0m", log, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteLine_WithoutAnsiColor_OmitsEscapeSequences()
    {
        using var output = new StringWriter();
        var reporter = new DevServerStatusReporter(output, prefix: "kiji dev", useEmoji: false, useAnsiColor: false);

        reporter.DevServerStarted(new Uri("http://127.0.0.1:8080/"), @"C:\site\contents", staticPath: null);

        var log = output.ToString();
        Assert.Contains("kiji dev    [Started] Started Kiji dev server at http://127.0.0.1:8080/", log, StringComparison.Ordinal);
        Assert.DoesNotContain(Esc, log, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangesDetected_WritesOneLinePerFile()
    {
        using var output = new StringWriter();
        var reporter = new DevServerStatusReporter(output, prefix: "kiji dev", useEmoji: true, useAnsiColor: false);

        reporter.ChangesDetected(
            [
                new WatchedChange(WatchedPathSource.Content, WatcherChangeTypes.Changed, $"posts{Path.DirectorySeparatorChar}entry.md"),
                new WatchedChange(WatchedPathSource.Static, WatcherChangeTypes.Created, $"images{Path.DirectorySeparatorChar}cover.png"),
            ],
            reloadedClientCount: 0);

        var log = output.ToString();
        Assert.Contains($"kiji dev    ⌚ File updated: .{Path.DirectorySeparatorChar}posts{Path.DirectorySeparatorChar}entry.md", log, StringComparison.Ordinal);
        Assert.Contains($"kiji dev    ⌚ File created: .{Path.DirectorySeparatorChar}images{Path.DirectorySeparatorChar}cover.png", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Detected file changes", log, StringComparison.Ordinal);
    }
}
