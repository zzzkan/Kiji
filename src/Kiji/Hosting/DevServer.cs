using Kiji.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Kiji.Hosting;

/// <summary>
/// On-demand development server. Pages render per request through the exact same
/// <c>HtmlRenderer</c> pipeline used by the static build; the only additions are
/// post-render live-reload script injection and content watching.
/// </summary>
internal sealed class DevServer(StaticSite app, DevServerStatusReporter? reporter = null) : IAsyncDisposable
{
    private const string DefaultUrl = "http://127.0.0.1:8080";

    private const int DebounceMilliseconds = 250;

    // Servers currently accepting live-reload clients; hot reload notifications
    // (see HotReloadHandler) fan out to every active instance.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<DevServer, byte> ActiveServers = new();

    private readonly LiveReloadHub _hub = new();
    private readonly DevServerStatusReporter _reporter = reporter ?? DevServerStatusReporter.CreateForCurrentProcess();
    private readonly string _displayRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Directory.GetCurrentDirectory()));
    private readonly Lock _snapshotLock = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly List<WatchedPath> _watchedPaths = [];
    private readonly List<WatchedChange> _pendingChanges = [];
    private SiteSnapshot? _snapshot;
    private Timer? _debounceTimer;
    private volatile bool _contentChanged;
    private WebApplication? _webApplication;
    private Task? _warmupTask;
    private SharedRenderLifetime? _sharedRenders;
    private bool _disposed;

    internal async Task<WebApplication> StartAsync(ResolvedSitePaths options, string[] args, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        // Kiji owns browser refresh, including content invalidation and BaseUrl.
        // The slim host does not run hosting startups, so dotnet watch's browser
        // refresh middleware is not injected. Watch still applies code updates
        // and rebuilds scoped CSS; Kiji reloads after those outputs change.
        var builder = WebApplication.CreateSlimBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // This host always serves live source files, including outside Development.
        // Its own fallback renders pages and discovers new assets without stealing routes.
        builder.Configuration["ReloadStaticAssetsAtRuntime"] = "true";
        builder.Configuration["DisableStaticAssetNotFoundRuntimeFallback"] = "true";
        builder.Configuration["EnableStaticAssetsDevelopmentCaching"] = "false";
        builder.Configuration["EnableStaticAssetsDevelopmentIntegrity"] = "false";

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
        _sharedRenders = new SharedRenderLifetime(cancellationToken, web.Lifetime.ApplicationStopping);

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

            await _hub.HandleClientAsync(context.WebSockets.AcceptWebSocketAsync, context.RequestAborted);
        });

        web.MapGet("/_kiji/livereload.js", static async context =>
        {
            context.Response.ContentType = "text/javascript; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(LiveReloadScript.Value, context.RequestAborted);
        });

        var assets = options.AssetManifestPath is null ? null
            : Assets.StaticAssetManifest.Load(options.AssetManifestPath, options.AssetEndpointsPath, app.Info.BaseUrl, includeIntegrity: false);
        app.UseAssetResources(assets?.Resources ?? Assets.AssetResources.Empty);
        if (assets?.ContentRoots.Length > 0)
        {
            // Match ASP.NET Core: SDK mappings are loaded at startup. Project/asset
            // configuration changes take effect on rebuild/restart; file bytes stay live.
            builder.Environment.WebRootFileProvider = new NullFileProvider();
            builder.Configuration[WebHostDefaults.StaticWebAssetsKey] = options.AssetManifestPath;
            StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
            if (builder.Environment.WebRootFileProvider is NullFileProvider)
            {
                throw new InvalidOperationException($"Static asset manifest '{options.AssetManifestPath}' could not be loaded. Rebuild the site.");
            }
            if (options.AssetEndpointsPath is not null && File.Exists(options.AssetEndpointsPath))
            {
                var files = assets.Assets.ToDictionary(static asset => asset.Target, static asset => asset.Source, StringComparer.OrdinalIgnoreCase);
                web.Use((context, next) => StaticAssetRequestHandler.HandleAsync(context, next, files));
                web.MapStaticAssets(options.AssetEndpointsPath);
            }
            // Discovery patterns also expose files added since the SDK manifest was built.
            web.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = builder.Environment.WebRootFileProvider,
                ServeUnknownFileTypes = true,
                OnPrepareResponse = static context => context.Context.Response.Headers.CacheControl = "no-store",
            });
            foreach (var root in assets.ContentRoots.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            {
                var filter = new StaticAssetWatchFilter(root, assets);
                WatchDirectory(root, WatchedPathSource.Static, filter.AffectsAsset);
            }
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

        var buildInputs = app.WatchedBuildInputs;
        foreach (var root in app.ContentRoots)
        {
            WatchDirectory(root, WatchedPathSource.Content);
        }
        foreach (var input in buildInputs)
        {
            WatchDirectory(input, WatchedPathSource.BuildInput);
        }
        StartWatchers();

        await web.StartAsync(cancellationToken);
        ActiveServers.TryAdd(this, 0);
        _reporter.DevServerStarted(
            new Uri(new Uri(web.Urls.First()), app.Info.BaseUrl.AbsolutePath),
            app.ContentRoots,
            assets?.ContentRoots.Length > 0,
            buildInputs);

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
    internal static Task NotifyCodeUpdated()
    {
        return Task.WhenAll(ActiveServers.Keys.Select(static server => server.ReloadAfterCodeUpdateAsync()));
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

        if (_sharedRenders is not null)
        {
            await _sharedRenders.DisposeAsync();
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
            // Keep directory-style page URLs canonical and aligned with the generated
            // route, even though page-bundle asset URLs do not depend on the slash.
            if (!path.EndsWith('/') && snapshot.PagesByRoute.TryGetValue(path + "/", out var slashPage))
            {
                await Results.LocalRedirect(
                        context.Request.PathBase.Add(PathString.FromUriComponent(slashPage.RoutePath)).ToUriComponent()
                        + context.Request.QueryString)
                    .ExecuteAsync(context);
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
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _sharedRenders!.Token);
        var html = await app.RenderPageAsync(page, cancellation.Token, _sharedRenders);
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

    private void WatchDirectory(string path, WatchedPathSource source, Func<string, bool>? affectsAsset = null)
    {
        _watchedPaths.Add(new WatchedPath(path, source, affectsAsset));
    }

    private void StartWatchers()
    {
        // Parent watchers also cover nested inputs. Share their native buffer and
        // callback, while retaining each input's filtering and invalidation rules.
        var remaining = _watchedPaths.Select(path => (Input: path, Root: path.WatchRoot))
            .OrderBy(static entry => entry.Root.Length).ToList();
        while (remaining.Count > 0)
        {
            var root = remaining[0].Root;
            var inputs = remaining.Where(entry => WatchedPath.Contains(root, entry.Root))
                .Select(static entry => entry.Input).ToArray();
            remaining.RemoveAll(entry => WatchedPath.Contains(root, entry.Root));
            if (!Directory.Exists(root)) { continue; }
            StartWatcher(root, inputs);
        }
    }

    private void StartWatcher(string root, WatchedPath[] inputs)
    {
        var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        };

        void HandleChange(object sender, FileSystemEventArgs args)
        {
            var oldPath = (args as RenamedEventArgs)?.OldFullPath;
            WatchedPath? affected = null;
            foreach (var input in inputs)
            {
                if (!input.Affects(args.FullPath, args.ChangeType)
                    && (oldPath is null || !input.Affects(oldPath, args.ChangeType))) { continue; }
                affected = input;
                // A shared static/content path still has to invalidate content.
                if (input.Source is not WatchedPathSource.Static) { break; }
            }
            if (affected is null) { return; }

            // Ignore directory timestamps generated alongside ordinary file saves.
            if (args.ChangeType is WatcherChangeTypes.Changed && Directory.Exists(args.FullPath)) { return; }
            ScheduleReload(CreateWatchedChange(affected.Source, args.ChangeType, args.FullPath, oldPath),
                affected.Source is not WatchedPathSource.Static);
        }

        watcher.Changed += HandleChange;
        watcher.Created += HandleChange;
        watcher.Deleted += HandleChange;
        watcher.Renamed += HandleChange;
        watcher.Error += (_, args) =>
        {
            var exception = args.GetException();
            foreach (var input in inputs)
            {
                if (exception is not null) { _reporter.WatcherError(input.Source, input.Path, exception); }
                // Lost events require conservative invalidation of every covered input.
                ScheduleReload(CreateWatchedChange(input.Source, WatcherChangeTypes.Changed, input.Path),
                    input.Source is not WatchedPathSource.Static);
            }
        };
        _watchers.Add(watcher);
        watcher.EnableRaisingEvents = true;
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
            if (_disposed)
            {
                return;
            }

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
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return [.. changes
            .GroupBy(static change => change.FullPath, pathComparer)
            .Select(static group =>
            {
                var latest = group.Last();
                var preferredSource = group.MinBy(static change => GetSourcePriority(change.Source))!.Source;
                return latest with { Source = preferredSource };
            })];
    }

    private WatchedChange CreateWatchedChange(
        WatchedPathSource source,
        WatcherChangeTypes changeType,
        string fullPath,
        string? oldFullPath = null)
    {
        fullPath = Path.GetFullPath(fullPath);
        return new WatchedChange(
            source,
            changeType,
            fullPath,
            GetDisplayPath(fullPath, _displayRoot),
            oldFullPath is null ? null : GetDisplayPath(Path.GetFullPath(oldFullPath), _displayRoot));
    }

    internal static string GetDisplayPath(string fullPath, string displayRoot)
    {
        fullPath = Path.GetFullPath(fullPath);
        displayRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(displayRoot));
        var relativePath = Path.GetRelativePath(displayRoot, fullPath);
        var parentPrefix = $"..{Path.DirectorySeparatorChar}";
        if (!Path.IsPathRooted(relativePath)
            && !string.Equals(relativePath, "..", StringComparison.Ordinal)
            && !relativePath.StartsWith(parentPrefix, StringComparison.Ordinal))
        {
            return string.Equals(relativePath, ".", StringComparison.Ordinal)
                ? relativePath
                : $".{Path.DirectorySeparatorChar}{relativePath}";
        }

        return fullPath;
    }

    private static int GetSourcePriority(WatchedPathSource source)
    {
        return source switch
        {
            WatchedPathSource.Content => 0,
            WatchedPathSource.BuildInput => 1,
            WatchedPathSource.Static => 2,
            _ => throw new InvalidOperationException($"Unknown watched path source '{source}'."),
        };
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
