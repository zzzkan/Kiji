using System.Collections.Concurrent;
using System.Net;
using Kiji.Tests.TestSite.ErrorHandling;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Xunit;

namespace Kiji.Tests;

public sealed class ErrorBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-errors-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("sync")]
    [InlineData("async")]
    [InlineData("render")]
    public async Task Boundary_WithoutLoggingProviders_RendersFallback(string failure)
    {
        await using var app = CreateApp();
        app.UseServices(services => services.AddLogging(logging => logging.ClearProviders()));
        app.AddPages<BoundaryPage>(_ => [new { Key = failure, Failure = failure }]);

        var html = await app.RenderPageAsync(Assert.Single(app.CreateSnapshot().Pages), CancellationToken.None);

        Assert.Contains($"Fallback for {failure}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Private failure", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Healthy", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultConsole_LogsWarningAndHigher_IncludingBoundaryExceptions()
    {
        using var logs = new RecordingLoggerProvider();
        await using var app = CreateApp();
        CaptureConsole(app, logs);
        app.AddPages<BoundaryPage>(_ => [new { Key = "default", Failure = "async" }]);

        await app.RenderPageAsync(Assert.Single(app.CreateSnapshot().Pages), CancellationToken.None);

        var warning = Assert.Single(logs.Entries);
        Assert.Equal(typeof(ErrorBoundary).FullName, warning.Category);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("Private failure for default", Assert.IsType<InvalidOperationException>(warning.Exception).Message);

        var logger = app.ServiceProvider.GetRequiredService<ILogger<BoundaryPage>>();
        foreach (var level in new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical })
        {
            logger.Log(level, new EventId(1), level, null, static (state, _) => state.ToString());
        }
        Assert.Equal([LogLevel.Warning, LogLevel.Error, LogLevel.Critical],
            logs.Entries.Where(entry => entry.Category == typeof(BoundaryPage).FullName).Select(entry => entry.Level));
    }

    [Theory]
    [InlineData(LogLevel.Warning, true)]
    [InlineData(LogLevel.Error, false)]
    public async Task Boundary_LogsThroughConfiguredProviderAndCategoryFilter(LogLevel minimum, bool expectWarning)
    {
        using var logs = new RecordingLoggerProvider();
        await using var app = CreateApp();
        CaptureConsole(app, logs);
        app.UseServices(services => services.AddLogging(logging => logging
            .ClearProviders()
            .AddProvider(logs)
            .SetMinimumLevel(LogLevel.Information)
            .AddFilter(typeof(ErrorBoundary).FullName, minimum)));
        app.AddPages<BoundaryPage>(_ => [new { Key = "logged", Failure = "async", CustomErrorContent = false }]);

        var html = await app.RenderPageAsync(Assert.Single(app.CreateSnapshot().Pages), CancellationToken.None);

        Assert.Contains("<div class=\"blazor-error-boundary\"></div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Private failure", html, StringComparison.Ordinal);
        var componentLog = Assert.Single(logs.Entries, entry => entry.Category == typeof(BoundaryChild).FullName);
        Assert.Equal(LogLevel.Information, componentLog.Level);
        Assert.Equal("Initializing logged", componentLog.Message);
        var boundaryLogs = logs.Entries.Where(entry => entry.Category == typeof(ErrorBoundary).FullName).ToArray();
        if (expectWarning)
        {
            var warning = Assert.Single(boundaryLogs);
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Equal(100, warning.EventId.Id);
            var exception = Assert.IsType<InvalidOperationException>(warning.Exception);
            Assert.Equal("Private failure for logged", exception.Message);
            Assert.Contains(exception.Message, warning.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(boundaryLogs);
        }
    }

    [Fact]
    public async Task Boundary_CustomLoggerUsesPageScope_AndRendersStayIsolated()
    {
        var loggers = new ConcurrentQueue<ScopedBoundaryLogger>();
        await using var app = CreateApp();
        app.UseServices(services => services.AddScoped<IErrorBoundaryLogger>(provider =>
        {
            var logger = new ScopedBoundaryLogger(provider.GetRequiredService<PageInfo>());
            loggers.Enqueue(logger);
            return logger;
        }));
        app.AddPages<BoundaryPage>(_ => [
            new { Key = "first", Failure = "async" },
            new { Key = "second", Failure = "sync" },
            new { Key = "healthy", Failure = "" },
        ]);
        var pages = app.CreateSnapshot().Pages;
        var renders = await Task.WhenAll(pages.Select(page => app.RenderPageAsync(page, CancellationToken.None)));
        for (var i = 0; i < pages.Count; i++)
        {
            var key = (string)pages[i].Parameters[nameof(BoundaryPage.Key)]!;
            Assert.Contains(key == "healthy" ? "Healthy healthy" : $"Fallback for {key}", renders[i], StringComparison.Ordinal);
        }

        Assert.Equal(3, loggers.Count);
        Assert.All(loggers, logger => Assert.True(logger.Disposed));
        foreach (var logger in loggers)
        {
            var key = logger.Page.Url.Segments[^1].TrimEnd('/');
            if (key == "healthy") { Assert.Empty(logger.Exceptions); }
            else { Assert.Equal($"Private failure for {key}", Assert.Single(logger.Exceptions).Message); }
        }

        var healthy = pages.Single(page => page.RoutePath == "/errors/healthy/");
        Assert.Contains("Healthy healthy", await app.RenderPageAsync(healthy, CancellationToken.None), StringComparison.Ordinal);
        Assert.Equal(4, loggers.Count);
        Assert.All(loggers, logger => Assert.True(logger.Disposed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Boundary_FallbackWorksInPublishAndDevelopment(bool development)
    {
        await using var app = CreateApp();
        app.AddPages<BoundaryPage>(_ => [new { Key = "page", Failure = "async" }]);

        string html;
        if (development)
        {
            var (server, web) = await app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None);
            await using var serverLifetime = server;
            using var client = new HttpClient { BaseAddress = new Uri(web.Urls.Single()) };
            using var response = await client.GetAsync(new Uri("/errors/page/", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            html = await response.Content.ReadAsStringAsync();
        }
        else
        {
            var output = Path.Combine(_root, "dist");
            await app.PublishAsync(output);
            html = await File.ReadAllTextAsync(Path.Combine(output, "errors", "page", "index.html"));
        }

        Assert.Contains("Fallback for page", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Private failure", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutBoundary_OriginalExceptionPropagates()
    {
        await using var app = CreateApp();
        app.AddPages<BoundaryPage>(_ => [new { Key = "unhandled", Failure = "async", UseBoundary = false }]);
        var page = Assert.Single(app.CreateSnapshot().Pages);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.RenderPageAsync(page, CancellationToken.None));

        Assert.Equal("Private failure for unhandled", exception.Message);
    }

    private StaticSite CreateApp()
    {
        Directory.CreateDirectory(_root);
        var app = StaticSite.Create([], new SiteExecutionPaths(_root));
        app.Info = new SiteInfo { Name = "Errors", BaseUrl = new Uri("https://example.com/") };
        return app;
    }

    private static void CaptureConsole(StaticSite app, RecordingLoggerProvider logs)
    {
        app.UseServices(services =>
        {
            services.AddSingleton(logs);
            // Change only the formatter: the console provider must come from Kiji's defaults.
            services.AddLogging(logging => logging.AddConsoleFormatter<RecordingConsoleFormatter, ConsoleFormatterOptions>());
            services.Configure<ConsoleLoggerOptions>(options => options.FormatterName = "test");
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }
}
