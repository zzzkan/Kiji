using Kiji.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kiji.Hosting;

/// <summary>
/// On-demand development server. Pages render per request through the exact same
/// <c>HtmlRenderer</c> pipeline used by the static build; the only additions are
/// post-render live-reload script injection and content watching.
/// </summary>
internal sealed class DevServer(SiteEngine app, WebApplicationBuilder webBuilder, DevServerStatusReporter reporter) : IAsyncDisposable
{
    private const string DefaultUrl = "http://127.0.0.1:8080";

    private const int DebounceMilliseconds = 250;

    // Servers currently accepting live-reload clients; hot reload notifications
    // (see HotReloadHandler) fan out to every active instance.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<DevServer, byte> ActiveServers = new();

    private readonly LiveReloadHub _hub = new();
    private readonly WebApplicationBuilder _builder = webBuilder;
    private readonly DevServerStatusReporter _reporter = reporter;
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
    private Task? _diagnosticsRelay;
    private SharedRenderLifetime? _sharedRenders;
    private bool _disposed;
    private Task _refreshTask = Task.CompletedTask;
    private CancellationTokenSource? _serverStopping;
    private string _imageOutput = null!;
    private readonly HashSet<string> _retiredImages = new(StringComparer.Ordinal);
    private readonly Dictionary<WatchedPath, (string Target, FileSystemWatcher Watcher)> _linkedWatchers = [];

    internal static DevServer Create(SiteEngine app, string[] args,
        DevServerStatusReporter? reporter = null, TextWriter? statusOutput = null, bool? isOutputRedirected = null)
    {
        // Kiji owns browser refresh, including content invalidation and BaseUrl.
        // The slim host does not run hosting startups, so dotnet watch's browser
        // refresh middleware is not injected. Watch still applies code updates
        // and rebuilds scoped CSS; Kiji reloads after those outputs change.
        // WatchDiagnosticsRelay takes only compilation errors from dotnet watch.
        var environment = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            // Both hosts use the site's environment and project directory.
            EnvironmentName = environment.EnvironmentName,
            ContentRootPath = environment.ContentRootPath,
        });
        return new DevServer(app, builder, reporter ?? DevServerStatusReporter.Create(
            builder.Configuration, statusOutput ?? Console.Out,
            isOutputRedirected ?? (statusOutput is not null || Console.IsOutputRedirected)));
    }

    internal async Task<WebApplication> StartAsync(ResolvedSitePaths options, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var imageOutput = _imageOutput = Path.Combine(options.OutputDirectory, "_images");
        // Built-in variants are immutable and can survive reloads and restarts.
        if (Directory.Exists(imageOutput))
        {
            foreach (var directory in Directory.EnumerateDirectories(imageOutput))
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                { _retiredImages.Add(file); }
            }
            CollectRetiredImages();
        }
        var work = Path.Combine(app.Paths.ResolveKijiPath(), "dev-work");
        if (Directory.Exists(work)) { Directory.Delete(work, recursive: true); }
        var builder = _builder;
        // The standard Host Filtering middleware reads AllowedHosts. Supply only a
        // fallback; appsettings, environment variables and command line stay in control.
        builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
        {
            InitialData = [new("AllowedHosts", "localhost;127.0.0.1;[::1]")],
        });
        // The site's logging, so builder.Logging and the Logging section also apply to ASP.NET Core.
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(app.ServiceProvider.GetRequiredService<ILoggerFactory>());
        // This host always serves live source files, including outside Development.
        // Its own fallback renders pages and discovers new assets without stealing routes.
        builder.Configuration["ReloadStaticAssetsAtRuntime"] = "true";
        builder.Configuration["DisableStaticAssetNotFoundRuntimeFallback"] = "true";
        builder.Configuration["EnableStaticAssetsDevelopmentCaching"] = "false";
        builder.Configuration["EnableStaticAssetsDevelopmentIntegrity"] = "false";
        // The developer exception page is outside the base path middleware, which
        // restores PathBase on the way out.
        builder.Services.AddSingleton<Microsoft.AspNetCore.Diagnostics.IDeveloperPageExceptionFilter>(
            new LiveReloadExceptionPageFilter(PathString.FromUriComponent(app.SiteInfo.BaseUrl.AbsolutePath.TrimEnd('/'))));

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
        _serverStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, web.Lifetime.ApplicationStopping);
        _sharedRenders = new SharedRenderLifetime(_serverStopping.Token, CancellationToken.None);

        // Must run before route matching, so the /_kiji/* endpoints below match a
        // prefixed request. WebApplication auto-inserts UseRouting ahead of all user
        // middleware when endpoints exist, unless the app calls UseRouting itself.
        web.UseSiteBasePath(app.SiteInfo.BaseUrl.AbsolutePath);
        web.UseRouting();

        web.UseWebSockets();

        web.Map("/_kiji/reload", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // Compilation errors include local paths, so only pages served here may
            // subscribe. Browsers always send Origin, and AllowedHosts rejects rebound hosts.
            string? origin = context.Request.Headers.Origin;
            if (!string.IsNullOrEmpty(origin)
                && !string.Equals(origin, $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
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
            : Assets.StaticAssetManifest.Load(options.AssetManifestPath, options.AssetEndpointsPath, app.SiteInfo.BaseUrl, includeIntegrity: false);
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

        // Shared generated assets (e.g. optimized images) are materialized into the output
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

        foreach (var root in app.ContentRoots)
        {
            WatchDirectory(root, WatchedPathSource.Content);
        }
        StartWatchers();

        await web.StartAsync(cancellationToken);
        ActiveServers.TryAdd(this, 0);
        _diagnosticsRelay = WatchDiagnosticsRelay.RunAsync(
            web.Configuration[WatchDiagnosticsRelay.EndpointKey],
            web.Configuration[WatchDiagnosticsRelay.ServerKeyKey],
            diagnostics => _hub.ReportDiagnosticsAsync(diagnostics, CancellationToken.None),
            _serverStopping.Token);
        _reporter.DevServerStarted(
            new Uri(new Uri(web.Urls.First()), app.SiteInfo.BaseUrl.AbsolutePath),
            app.ContentRoots,
            assets?.ContentRoots.Length > 0);

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
            if (_disposed) { return; }
            QueueContentRefresh();
        }
        await _refreshTask;

        var reloadedClients = await _hub.BroadcastReloadAsync(CancellationToken.None);
        _reporter.CodeUpdated(reloadedClients);
    }

    public async ValueTask DisposeAsync()
    {
        ActiveServers.TryRemove(this, out _);
        lock (_snapshotLock)
        {
            if (_disposed) { return; }
            _disposed = true;
        }

        if (_debounceTimer is not null)
        {
            await _debounceTimer.DisposeAsync();
        }

        if (_serverStopping is not null) { await _serverStopping.CancelAsync(); }
        await _refreshTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        foreach (var watcher in _watchers) { watcher.Dispose(); }
        foreach (var (_, watcher) in _linkedWatchers.Values) { watcher.Dispose(); }

        if (_webApplication is not null)
        {
            await _webApplication.StopAsync();
        }
        if (_sharedRenders is not null) { await _sharedRenders.DisposeAsync(); }
        if (_webApplication is not null) { await _webApplication.DisposeAsync(); }

        if (_warmupTask is not null)
        {
            await _warmupTask;
        }

        if (_diagnosticsRelay is not null)
        {
            await _diagnosticsRelay;
        }
        _serverStopping?.Dispose();
    }

    private static readonly Lazy<string> LiveReloadScript = new(static () =>
    {
        using var stream = typeof(DevServer).Assembly.GetManifestResourceStream("Kiji.Hosting.livereload.js")
            ?? throw new InvalidOperationException("Embedded live reload script not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    internal async Task HandlePageAsync(HttpContext context)
    {
        try
        {
            while (true)
            {
                Task refresh;
                Task<string>? render = null;
                SharedRenderLifetime? lifetime = null;
                Rendering.PageRenderRequest? page = null;
                var status = StatusCodes.Status200OK;
                lock (_snapshotLock)
                {
                    if (_disposed || _serverStopping!.IsCancellationRequested)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        return;
                    }
                    refresh = _refreshTask;
                    if (refresh.IsCompletedSuccessfully)
                    {
                        var snapshot = GetSnapshot();
                        var path = context.Request.Path.Value ?? "/";
                        if (!snapshot.PagesByRoute.TryGetValue(path, out page))
                        {
                            if (!path.EndsWith('/') && snapshot.PagesByRoute.TryGetValue(path + "/", out var slashPage))
                            {
                                context.Response.Redirect(context.Request.PathBase.Add(PathString.FromUriComponent(slashPage.RoutePath)).ToUriComponent()
                                    + context.Request.QueryString);
                                return;
                            }
                            status = StatusCodes.Status404NotFound;
                            snapshot.PagesByRoute.TryGetValue("/404.html", out page);
                        }
                        if (page is null) { context.Response.StatusCode = status; return; }
                        lifetime = _sharedRenders!;
                        render = lifetime.RunAsync(async token =>
                        {
                            // Register only rendering under the lock; sending the response
                            // belongs to the request and cannot delay a content refresh.
                            await Task.Yield();
                            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, token);
                            return await app.RenderPageAsync(page, cancellation.Token, lifetime);
                        });
                    }
                }
                if (render is null) { await refresh.WaitAsync(context.RequestAborted); continue; }
                string html;
                try { html = await render; }
                catch (OperationCanceledException) when (lifetime!.IsStopping
                    && !context.RequestAborted.IsCancellationRequested && !_serverStopping!.IsCancellationRequested)
                {
                    // The snapshot changed before any response bytes were sent.
                    continue;
                }
                context.Response.StatusCode = status;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync(InjectLiveReloadScript(html, context.Request.PathBase), context.RequestAborted);
                return;
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested || _serverStopping!.IsCancellationRequested)
        {
            if (!context.Response.HasStarted) { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; }
        }
    }

    // PathBase carries the site's base path, so the script resolves under the prefix.
    // With no prefix its Value is null and the tag is byte-identical to a plain
    // "/_kiji/livereload.js" reference.
    internal static string InjectLiveReloadScript(string html, PathString pathBase)
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
            _watchers.Add(StartWatcher(root, inputs));
        }
        RefreshLinkedWatchers();
    }

    private void RefreshLinkedWatchers()
    {
        // Parent watchers survive ordinary saves. Only a retargeted content link
        // needs a new direct watcher; Error events never restart native watchers.
        foreach (var input in _watchedPaths.Where(input => input.Source == WatchedPathSource.Content))
        {
            try
            {
                RefreshLinkedWatcher(input);
            }
            // A target removed while its watcher starts surfaces as ArgumentException.
            // A faulted refresh would fail every request until the next content change.
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _reporter.WatcherError(input.Source, input.Path, exception);
            }
        }
    }

    private void RefreshLinkedWatcher(WatchedPath input)
    {
        var directory = new DirectoryInfo(input.Path);
        var target = directory.LinkTarget is not null
            ? directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName : null;
        if (_linkedWatchers.TryGetValue(input, out var existing))
        {
            if (existing.Target == target) { return; }
            existing.Watcher.Dispose();
            _linkedWatchers.Remove(input);
        }
        if (target is not null)
        {
            // Watch the physical target from its parent, so replacing that directory
            // at the same path cannot strand a watcher on the retired inode.
            var physical = new WatchedPath(target, input.Source, affectsAsset: null);
            _linkedWatchers.Add(input, (target, StartWatcher(physical.WatchRoot, [physical])));
        }
    }

    private FileSystemWatcher StartWatcher(string root, WatchedPath[] inputs)
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
        try
        {
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch { watcher.Dispose(); throw; }
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
                QueueContentRefresh();
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
            WatchedPathSource.Static => 1,
            _ => throw new InvalidOperationException($"Unknown watched path source '{source}'."),
        };
    }

    // Called under the snapshot lock. New requests wait while old renders are
    // canceled and joined; cleanup can never race a writer from either snapshot.
    private void QueueContentRefresh()
    {
        var previous = _refreshTask;
        _refreshTask = Task.Run(async () =>
        {
            // Retire source work before invalidating the snapshot.
            await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await _sharedRenders!.DisposeAsync();
            lock (_snapshotLock)
            {
                if (_disposed) { return; }
                foreach (var path in _sharedRenders.Outputs) { _retiredImages.Add(path); }
                app.InvalidateContent();
                _snapshot = null;
                _sharedRenders = new SharedRenderLifetime(_serverStopping!.Token, CancellationToken.None);
            }
            // Native watcher shutdown may join a callback that needs _snapshotLock.
            // Requests still wait for this refresh; server disposal joins it as well.
            CollectRetiredImages();
            RefreshLinkedWatchers();
        });
    }

    private void CollectRetiredImages()
    {
        foreach (var path in _retiredImages.ToArray())
        {
            try
            {
                File.Delete(path);
                var directory = Path.GetDirectoryName(path);
                while (directory is not null && directory != _imageOutput && Directory.Exists(directory)
                    && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                    directory = Path.GetDirectoryName(directory);
                }
                _retiredImages.Remove(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A static response or scanner may still hold the file on Windows.
                // Retry next refresh without preventing fresh pages or reload notices.
            }
        }
    }

    private async Task ReportReloadAsync(List<WatchedChange> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }

        await _refreshTask;
        var reloadedClients = await _hub.BroadcastReloadAsync(CancellationToken.None);
        _reporter.ChangesDetected(changes, reloadedClients);
    }
}
