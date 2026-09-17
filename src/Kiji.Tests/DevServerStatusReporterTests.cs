using Kiji.Hosting;
using Xunit;

namespace Kiji.Tests;

public sealed class DevServerStatusReporterTests
{
    [Fact]
    public void PlainOutput_ReportsAddressAndWatcherFailureWithoutAnsi()
    {
        using var output = new StringWriter();
        var reporter = new DevServerStatusReporter(output, prefix: "kiji", useEmoji: false, useAnsiColor: false);
        reporter.DevServerStarted(
            new Uri("http://127.0.0.1:8080/"),
            @"C:\site\contents",
            staticPath: null,
            [@"C:\site\data\authors.json"]);
        reporter.WatcherError(WatchedPathSource.Content, @"C:\site\contents", new IOException("access denied"));

        var log = output.ToString();
        Assert.Contains("http://127.0.0.1:8080/", log, StringComparison.Ordinal);
        Assert.Contains(@"C:\site\contents", log, StringComparison.Ordinal);
        Assert.Contains(@"Watching build input: 'C:\site\data\authors.json'.", log, StringComparison.Ordinal);
        Assert.Contains("access denied", log, StringComparison.Ordinal);
        Assert.DoesNotContain(((char)0x1b).ToString(), log, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangesDetected_ReportsSemanticSourceRenameAndSkippedReload()
    {
        using var output = new StringWriter();
        var reporter = new DevServerStatusReporter(output, prefix: "kiji", useEmoji: false, useAnsiColor: false);
        WatchedChange[] changes =
        [
            new(
                WatchedPathSource.Content,
                WatcherChangeTypes.Renamed,
                @"C:\site\contents\new.md",
                @"contents\new.md",
                @"contents\old.md"),
        ];

        reporter.ChangesDetected(changes, reloadedClientCount: 0);

        var log = output.ToString();
        Assert.Contains(@"Content renamed: contents\old.md -> contents\new.md", log, StringComparison.Ordinal);
        Assert.Contains("Browser reload skipped: no clients connected", log, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeUpdated_ReportsCacheRefreshWhenNoBrowserIsConnected()
    {
        using var output = new StringWriter();
        var reporter = new DevServerStatusReporter(output, prefix: "kiji", useEmoji: false, useAnsiColor: false);

        reporter.CodeUpdated(reloadedClientCount: 0);

        Assert.Contains(
            "Page cache refreshed after a code update; browser reload skipped: no clients connected.",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DevServerStarted_EmphasizesAddressWhenAnsiIsEnabled()
    {
        using var output = new StringWriter();
        var reporter = new DevServerStatusReporter(output, prefix: "kiji", useEmoji: true, useAnsiColor: true);

        reporter.DevServerStarted(
            new Uri("http://127.0.0.1:8080/kiji/"),
            @"C:\site\contents",
            @"C:\site\wwwroot",
            []);

        Assert.Contains(
            "\u001b[1;4:4;36mhttp://127.0.0.1:8080/kiji/\u001b[0m",
            output.ToString(),
            StringComparison.Ordinal);
    }
}
