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
internal sealed class DevServer(KijiApp app) : IAsyncDisposable
{
    private const string LiveReloadScriptTag = """<script src="/_kiji/livereload.js" defer></script>""";
    private const int DebounceMilliseconds = 250;

    private readonly LiveReloadHub _hub = new();
    private readonly Lock _snapshotLock = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private SiteSnapshot? _snapshot;
    private Timer? _debounceTimer;
    private volatile bool _contentChanged;
    private WebApplication? _webApplication;

    internal async Task<WebApplication> StartAsync(SsgOptions options, int port, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(options.OutputPath, options.AssetsDirectoryName));

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        var web = builder.Build();
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

        web.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(options.StaticPath),
            ServeUnknownFileTypes = true,
            OnPrepareResponse = static context => context.Context.Response.Headers.CacheControl = "no-store",
        });

        web.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(options.OutputPath, options.AssetsDirectoryName)),
            RequestPath = "/" + options.AssetsDirectoryName,
            ServeUnknownFileTypes = true,
            OnPrepareResponse = static context => context.Context.Response.Headers.CacheControl = "no-store",
        });

        web.MapFallback(HandlePageAsync);

        WatchDirectory(options.ContentsPath, contentDirectory: true);
        WatchDirectory(options.StaticPath, contentDirectory: false);

        await web.StartAsync(cancellationToken);
        _webApplication = web;
        return web;
    }

    public async ValueTask DisposeAsync()
    {
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
            // Mirror Cloudflare's auto-trailing-slash behavior so dev matches production.
            if (!path.EndsWith('/') && snapshot.PagesByRoute.ContainsKey(path + "/"))
            {
                context.Response.Redirect(path + "/" + context.Request.QueryString, permanent: true);
                context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
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
        html = InjectLiveReloadScript(html);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(html, context.RequestAborted);
    }

    private static string InjectLiveReloadScript(string html)
    {
        var bodyCloseIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyCloseIndex >= 0
            ? html.Insert(bodyCloseIndex, LiveReloadScriptTag)
            : html + LiveReloadScriptTag;
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

    private void WatchDirectory(string path, bool contentDirectory)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        };

        void HandleChange(object sender, FileSystemEventArgs args)
        {
            ScheduleReload(contentDirectory);
        }

        watcher.Changed += HandleChange;
        watcher.Created += HandleChange;
        watcher.Deleted += HandleChange;
        watcher.Renamed += (_, _) => ScheduleReload(contentDirectory);
        watcher.EnableRaisingEvents = true;

        _watchers.Add(watcher);
    }

    private void ScheduleReload(bool contentChanged)
    {
        if (contentChanged)
        {
            _contentChanged = true;
        }

        // Editors fire multiple events per save; debounce before reloading.
        _debounceTimer ??= new Timer(_ => OnDebounceElapsed(), state: null, Timeout.Infinite, Timeout.Infinite);
        _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
    }

    private void OnDebounceElapsed()
    {
        if (_contentChanged)
        {
            _contentChanged = false;
            lock (_snapshotLock)
            {
                _snapshot = null;
                app.InvalidateContent();
            }
        }

        _ = _hub.BroadcastReloadAsync(CancellationToken.None);
    }
}
