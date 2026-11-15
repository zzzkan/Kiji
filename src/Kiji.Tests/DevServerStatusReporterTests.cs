using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kiji.Tests;

public sealed class DevServerStatusReporterTests
{
    [Theory]
    [InlineData(null, null, null, false, true, true)]
    [InlineData("1", null, null, false, false, true)]
    [InlineData("true", null, null, false, false, true)]
    [InlineData("false", "0", "xterm", false, true, true)]
    [InlineData(null, "1", null, false, true, false)]
    [InlineData(null, null, "DUMB", false, true, false)]
    [InlineData(null, null, null, true, true, false)]
    public void Configuration_ControlsEmojiAndColor(string? suppressEmoji, string? noColor,
        string? term, bool redirected, bool emoji, bool color)
    {
        // A later provider can override the defaults without changing process-wide state.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1",
                ["NO_COLOR"] = "1",
                ["TERM"] = "dumb",
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DOTNET_WATCH_SUPPRESS_EMOJIS"] = suppressEmoji,
                ["NO_COLOR"] = noColor,
                ["TERM"] = term,
            }).Build();
        using var output = new StringWriter();
        var reporter = DevServerStatusReporter.Create(configuration, output, redirected);

        reporter.DevServerStarted(new Uri("http://localhost/"), [], watchesStaticAssets: false);

        var log = output.ToString();
        Assert.Equal(emoji, log.Contains("🚀", StringComparison.Ordinal));
        Assert.Equal(color, log.Contains('\u001b'));
    }

    [Fact]
    public void PlainOutput_ReportsAddressAndWatcherFailureWithoutAnsi()
    {
        using var output = new StringWriter();
        var reporter = new DevServerStatusReporter(output, prefix: "kiji", useEmoji: false, useAnsiColor: false);
        reporter.DevServerStarted(
            new Uri("http://127.0.0.1:8080/"),
            [@"C:\site\content"],
            watchesStaticAssets: false);
        reporter.WatcherError(WatchedPathSource.Content, @"C:\site\content", new IOException("access denied"));

        var log = output.ToString();
        Assert.Contains("http://127.0.0.1:8080/", log, StringComparison.Ordinal);
        Assert.Contains(@"C:\site\content", log, StringComparison.Ordinal);
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
                @"C:\site\content\new.md",
                @"content\new.md",
                @"content\old.md"),
        ];

        reporter.ChangesDetected(changes, reloadedClientCount: 0);

        var log = output.ToString();
        Assert.Contains(@"Content renamed: content\old.md -> content\new.md", log, StringComparison.Ordinal);
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
            [@"C:\site\content"],
            watchesStaticAssets: true);

        Assert.Contains(
            "\u001b[1;4:4;36mhttp://127.0.0.1:8080/kiji/\u001b[0m",
            output.ToString(),
            StringComparison.Ordinal);
    }
}
