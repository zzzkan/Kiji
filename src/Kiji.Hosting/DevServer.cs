using Kiji.Images;
using Kiji.Paths;
using Kiji.Rendering;
using Kiji.StaticAssets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kiji.Hosting;

internal sealed class DevServer(SiteEngine app, WebApplicationBuilder webBuilder, DevServerStatusReporter reporter) : IAsyncDisposable
{
    private const string DefaultUrl = "http://127.0.0.1:8080";

    private const int DebounceMilliseconds = 250;

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
        var environment = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
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
        if (Directory.Exists(imageOutput))
        {
            foreach (var directory in Directory.EnumerateDirectories(imageOutput))
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                { _retiredImages.Add(file); }
            }
            CollectRetiredImages();
        }
        ImageArtifactProcessor.ClearWork(app.Paths.ResolveImageWorkPath(options));
        var builder = _builder;
        builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
        {
            InitialData = [new("AllowedHosts", "localhost;127.0.0.1;[::1]")],
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(app.ServiceProvider.GetRequiredService<ILoggerFactory>());
        builder.Configuration["ReloadStaticAssetsAtRuntime"] = "true";
        builder.Configuration["DisableStaticAssetNotFoundRuntimeFallback"] = "true";
        builder.Configuration["EnableStaticAssetsDevelopmentCaching"] = "false";
        builder.Configuration["EnableStaticAssetsDevelopmentIntegrity"] = "false";
        builder.Services.AddSingleton<Microsoft.AspNetCore.Diagnostics.IDeveloperPageExceptionFilter>(
            new LiveReloadExceptionPageFilter(PathString.FromUriComponent(app.SiteInfo.BaseUrl.AbsolutePath.TrimEnd('/'))));

        if (string.IsNullOrEmpty(builder.Configuration["urls"]))
        {
            builder.WebHost.UseUrls(DefaultUrl);
        }

        var web = builder.Build();
        _webApplication = web;
        _serverStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, web.Lifetime.ApplicationStopping);
        _sharedRenders = new SharedRenderLifetime(_serverStopping.Token, CancellationToken.None);

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
            : StaticAssetManifest.Load(options.AssetManifestPath, options.AssetEndpointsPath, app.SiteInfo.BaseUrl, includeIntegrity: false);
        app.UseAssetResources(assets?.Resources ?? AssetResources.Empty);
        if (assets?.ContentRoots.Length > 0)
        {
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

        web.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(options.OutputDirectory),
            ServeUnknownFileTypes = true,
            OnPrepareResponse = static context => context.Context.Response.Headers.CacheControl = "no-store",
        });

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

        _warmupTask = Task.Run(() =>
        {
            try
            {
                GetSnapshot();
            }
            catch
            {
            }
        }, CancellationToken.None);

        return web;
    }

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
                string? redirect = null;
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
                                redirect = context.Request.PathBase.Add(PathString.FromUriComponent(slashPage.RoutePath)).ToUriComponent()
                                    + context.Request.QueryString;
                            }
                            else
                            {
                                status = StatusCodes.Status404NotFound;
                                snapshot.PagesByRoute.TryGetValue("/404.html", out page);
                            }
                        }
                        if (redirect is null && page is null) { context.Response.StatusCode = status; return; }
                        if (redirect is null)
                        {
                            lifetime = _sharedRenders!;
                            render = lifetime.RunAsync(async token =>
                            {
                                await Task.Yield();
                                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, token);
                                return await app.RenderPageAsync(page!, cancellation.Token, lifetime);
                            });
                        }
                    }
                }
                if (redirect is not null)
                {
                    await Results.LocalRedirect(redirect).ExecuteAsync(context);
                    return;
                }
                if (render is null) { await refresh.WaitAsync(context.RequestAborted); continue; }
                string html;
                try { html = await render; }
                catch (OperationCanceledException) when (lifetime!.IsStopping
                    && !context.RequestAborted.IsCancellationRequested && !_serverStopping!.IsCancellationRequested)
                {
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
        foreach (var input in _watchedPaths.Where(input => input.Source == WatchedPathSource.Content))
        {
            try
            {
                RefreshLinkedWatcher(input);
            }
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
                if (input.Source is not WatchedPathSource.Static) { break; }
            }
            if (affected is null) { return; }

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

    private void QueueContentRefresh()
    {
        var previous = _refreshTask;
        _refreshTask = Task.Run(async () =>
        {
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
