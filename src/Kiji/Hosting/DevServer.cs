using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Kiji.Hosting;

/// <summary>
/// On-demand development server. Pages render per request through the exact same
/// <c>HtmlRenderer</c> pipeline used by the static build; the only additions are
/// post-render live-reload script injection and content watching.
/// </summary>
internal sealed class DevServer(StaticSite app) : IAsyncDisposable
{
    private const string DefaultUrl = "http://127.0.0.1:8080";

    private const int DebounceMilliseconds = 250;

    // Servers currently accepting live-reload clients; hot reload notifications
    // (see HotReloadHandler) fan out to every active instance.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<DevServer, byte> ActiveServers = new();

    private readonly LiveReloadHub _hub = new();
    private readonly DevServerStatusReporter _reporter = DevServerStatusReporter.CreateForCurrentProcess();
    private readonly Lock _snapshotLock = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly List<WatchedChange> _pendingChanges = [];
    private SiteSnapshot? _snapshot;
    private Timer? _debounceTimer;
    private volatile bool _contentChanged;
    private WebApplication? _webApplication;
    private Task? _warmupTask;
    private bool _disposed;

    internal DevServer(StaticSite app, DevServerStatusReporter? reporter = null)
        : this(app)
    {
        _reporter = reporter ?? DevServerStatusReporter.CreateForCurrentProcess();
    }

    internal async Task<WebApplication> StartAsync(ResolvedSitePaths options, string[] args, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        Directory.CreateDirectory(options.StaticDirectory);

        var builder = WebApplication.CreateSlimBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Only fill in an address when nothing else supplied one, so ASPNETCORE_URLS,
        // --urls, and launchSettings.json behave exactly as they do for any other
        // ASP.NET Core app. Loopback rather than localhost: the dev server is not
        // something to expose on the network.
        if (string.IsNullOrEmpty(builder.Configuration["urls"]))
        {
            builder.WebHost.UseUrls(DefaultUrl);
        }

        var web = builder.Build();
        _webApplication = web;

        // Must run before route matching, so the /_kiji/* endpoints below match a
        // prefixed request. WebApplication auto-inserts UseRouting ahead of all user
        // middleware when endpoints exist, unless the app calls UseRouting itself.
        web.UseSiteBasePath(app.Info.BaseUrl.AbsolutePath);
        web.UseRouting();

        web.UseWebSockets();

        web.Map("/_kiji/reload", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await _hub.HandleClientAsync(socket, context.RequestAborted);
        });

        web.MapGet("/_kiji/livereload.js", static async context =>
        {
            context.Response.ContentType = "text/javascript; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(LiveReloadScript.Value, context.RequestAborted);
        });

        if (Directory.Exists(options.StaticDirectory))
        {
            web.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(options.StaticDirectory),
                ServeUnknownFileTypes = true,
                OnPrepareResponse = static context => context.Context.Response.Headers.CacheControl = "no-store",
            });
        }

        // Page-bundle assets (e.g. optimized images) are materialized into the output
        // mirror during on-demand page renders and served from there.
        web.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(options.OutputDirectory),
            ServeUnknownFileTypes = true,
            OnPrepareResponse = static context => context.Context.Response.Headers.CacheControl = "no-store",
        });

        // Leave unmatched requests without an endpoint while static files run.
        // A routed catch-all makes StaticFileMiddleware skip file requests, and
        // MapFallback's default nonfile constraint excludes pages such as /404.html.
        web.Use(async (context, next) =>
        {
            if (context.GetEndpoint() is null)
            {
                await HandlePageAsync(context);
            }
            else
            {
                await next(context);
            }
        });

        WatchDirectory(options.ContentDirectory, WatchedPathSource.Content);
        WatchDirectory(options.StaticDirectory, WatchedPathSource.Static);
        foreach (var input in app.WatchedBuildInputs)
        {
            WatchDirectory(input, WatchedPathSource.BuildInput);
        }

        await web.StartAsync(cancellationToken);
        ActiveServers.TryAdd(this, 0);
        _reporter.DevServerStarted(
            new Uri(new Uri(web.Urls.First()), app.Info.BaseUrl.AbsolutePath),
            options.ContentDirectory,
            Directory.Exists(options.StaticDirectory) ? options.StaticDirectory : null);

        // Warm the snapshot (page discovery + content materialization) in the
        // background so the first request doesn't pay for it. Failures are ignored
        // here; the first request recomputes and surfaces the real error.
        _warmupTask = Task.Run(() =>
        {
            try
            {
                GetSnapshot();
            }
            catch
            {
                // Reported on first request.
            }
        }, CancellationToken.None);

        return web;
    }

    /// <summary>
    /// Called after a hot reload (e.g. dotnet watch) applied code updates to this
    /// process: drops cached snapshots so updated components render fresh, then
    /// reloads connected browsers.
    /// </summary>
    internal static void NotifyCodeUpdated()
    {
        foreach (var server in ActiveServers.Keys)
        {
            _ = server.ReloadAfterCodeUpdateAsync();
        }
    }

    internal async Task ReloadAfterCodeUpdateAsync()
    {
        lock (_snapshotLock)
        {
            _snapshot = null;
            app.InvalidateContent();
        }

        var reloadedClients = await _hub.BroadcastReloadAsync(CancellationToken.None);
        _reporter.CodeUpdated(reloadedClients);
    }

    public async ValueTask DisposeAsync()
    {
        ActiveServers.TryRemove(this, out _);
        lock (_snapshotLock)
        {
            _disposed = true;
        }

        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        if (_debounceTimer is not null)
        {
            await _debounceTimer.DisposeAsync();
        }

        if (_webApplication is not null)
        {
            await _webApplication.DisposeAsync();
        }

        if (_warmupTask is not null)
        {
            await _warmupTask;
        }
    }

    private static readonly Lazy<string> LiveReloadScript = new(static () =>
    {
        using var stream = typeof(DevServer).Assembly.GetManifestResourceStream("Kiji.Hosting.livereload.js")
            ?? throw new InvalidOperationException("Embedded live reload script not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private async Task HandlePageAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var snapshot = GetSnapshot();

        if (!snapshot.PagesByRoute.TryGetValue(path, out var page))
        {
            // Directory-style pages must have a trailing slash in the browser:
            // page-bundle URLs such as ./cover.webp resolve relative to that URL.
            if (!path.EndsWith('/') && snapshot.PagesByRoute.TryGetValue(path + "/", out var slashPage))
            {
                context.Response.Redirect(context.Request.PathBase.Add(PathString.FromUriComponent(slashPage.RoutePath)).ToUriComponent()
                    + context.Request.QueryString);
                return;
            }

            if (snapshot.PagesByRoute.TryGetValue("/404.html", out var notFoundPage))
            {
                await WritePageAsync(context, notFoundPage, StatusCodes.Status404NotFound);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await WritePageAsync(context, page, StatusCodes.Status200OK);
    }

    private async Task WritePageAsync(HttpContext context, Rendering.PageRenderRequest page, int statusCode)
    {
        var html = await app.RenderPageAsync(page, context.RequestAborted);
        html = InjectLiveReloadScript(html, context.Request.PathBase);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(html, context.RequestAborted);
    }

    // PathBase carries the site's base path, so the script resolves under the prefix.
    // With no prefix its Value is null and the tag is byte-identical to a plain
    // "/_kiji/livereload.js" reference.
    private static string InjectLiveReloadScript(string html, PathString pathBase)
    {
        var scriptPath = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(pathBase.ToUriComponent() + "/_kiji/livereload.js");
        var tag = $"""<script src="{scriptPath}" defer></script>""";
        var bodyCloseIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyCloseIndex >= 0
            ? html.Insert(bodyCloseIndex, tag)
            : html + tag;
    }

    private SiteSnapshot GetSnapshot()
    {
        var snapshot = _snapshot;
        if (snapshot is not null)
        {
            return snapshot;
        }

        lock (_snapshotLock)
        {
            return _snapshot ??= app.CreateSnapshot();
        }
    }

    private void WatchDirectory(string path, WatchedPathSource source)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        // Watch an existing parent, so creating, deleting or replacing the source
        // directory itself does not permanently detach the watcher.
        var ancestor = Directory.GetParent(path);
        while (ancestor is not null && !ancestor.Exists)
        {
            ancestor = ancestor.Parent;
        }

        var watchRoot = ancestor?.FullName ?? path;
        if (!Directory.Exists(watchRoot))
        {
            return;
        }

        var watcher = new FileSystemWatcher(watchRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        };

        void HandleChange(object sender, FileSystemEventArgs args)
        {
            if (!IsWatchedPath(args.FullPath) && (args is not RenamedEventArgs renamed || !IsWatchedPath(renamed.OldFullPath)))
            {
                return;
            }

            // A file save also touches its parent directory's timestamp, raising a
            // second Changed event for the directory itself; only files matter here.
            if (args.ChangeType is WatcherChangeTypes.Changed && Directory.Exists(args.FullPath))
            {
                return;
            }

            ScheduleReload(new WatchedChange(source, args.ChangeType, Path.GetRelativePath(path, args.FullPath)), source is not WatchedPathSource.Static);
        }

        bool IsWatchedPath(string candidate)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(candidate, path, comparison)
                || candidate.StartsWith(path + Path.DirectorySeparatorChar, comparison);
        }

        watcher.Changed += HandleChange;
        watcher.Created += HandleChange;
        watcher.Deleted += HandleChange;
        watcher.Renamed += HandleChange;
        watcher.Error += (_, args) =>
        {
            var exception = args.GetException();
            if (exception is not null)
            {
                _reporter.WatcherError(source, path, exception);
            }
            // Events may have been lost (e.g. buffer overflow). Rebuild the snapshot
            // conservatively instead of continuing to serve potentially stale content.
            ScheduleReload(new WatchedChange(source, WatcherChangeTypes.Changed, "."), source is not WatchedPathSource.Static);
        };
        watcher.EnableRaisingEvents = true;

        _watchers.Add(watcher);
    }

    private void ScheduleReload(WatchedChange change, bool contentChanged)
    {
        // Editors fire multiple events per save; debounce before reloading. Watcher
        // callbacks arrive on thread-pool threads, so timer creation must be locked.
        lock (_snapshotLock)
        {
            if (_disposed)
            {
                return;
            }

            _contentChanged |= contentChanged;
            _pendingChanges.Add(change);
            _debounceTimer ??= new Timer(_ => OnDebounceElapsed(), state: null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void OnDebounceElapsed()
    {
        List<WatchedChange> changes;

        lock (_snapshotLock)
        {
            changes = DeduplicateChanges(_pendingChanges);
            _pendingChanges.Clear();

            if (_contentChanged)
            {
                _contentChanged = false;
                _snapshot = null;
                app.InvalidateContent();
            }
        }

        _ = ReportReloadAsync(changes);
    }

    // Editors fire several watcher events per save (and debounce collects them all),
    // so collapse to one entry per file, keeping the latest change type.
    internal static List<WatchedChange> DeduplicateChanges(IEnumerable<WatchedChange> changes)
    {
        return [.. changes
            .GroupBy(static change => (change.Source, change.Path))
            .Select(static group => group.Last())];
    }

    private async Task ReportReloadAsync(List<WatchedChange> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }

        var reloadedClients = await _hub.BroadcastReloadAsync(CancellationToken.None);
        _reporter.ChangesDetected(changes, reloadedClients);
    }
}
