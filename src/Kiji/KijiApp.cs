using System.Reflection;
using System.Runtime.InteropServices;
using Kiji.Assets;
using Kiji.Generation;
using Kiji.Hosting;
using Kiji.Rendering;
using Kiji.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kiji;

/// <summary>
/// A Kiji static site application. Created via <see cref="KijiBuilder.Build"/>;
/// declare page mappings with the <c>Map*</c> methods, then dispatch commands
/// (<c>build</c>, <c>clean</c>, <c>serve</c>, <c>preview</c>) with <see cref="RunAsync"/>.
/// </summary>
public sealed class KijiApp : IAsyncDisposable
{
    private const string RouteDataParameterName = "RouteData";

    private readonly KijiBuilder _builder;
    private readonly List<RouteRegistration> _routeRegistrations = [];
    private readonly List<Func<IReadOnlyDictionary<string, string>, IReadOnlyList<FeedEntry>>> _feedRegistrations = [];
    private Type? _rootComponentType;
    private Assembly? _pageAssembly;
    private Type? _notFoundComponentType;
    private ServiceProvider? _services;
    private ComponentRenderer? _renderer;
    private SsgOptions? _serveOptions;

    internal KijiApp(KijiBuilder builder)
    {
        _builder = builder;
        Site = builder.Site!;
    }

    /// <summary>
    /// The site metadata configured on the builder.
    /// </summary>
    public SiteInfo Site { get; }

    /// <summary>
    /// Creates a new <see cref="KijiBuilder"/>.
    /// </summary>
    /// <param name="args">Command line arguments; forwarded to <see cref="RunAsync"/> for command dispatch.</param>
    public static KijiBuilder CreateBuilder(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new KijiBuilder(args);
    }

    /// <summary>
    /// Registers the root document component and discovers all <c>@page</c> components
    /// in its assembly. The root component must declare a <c>RouteData</c> parameter.
    /// </summary>
    public KijiApp MapPages<TRoot>()
        where TRoot : IComponent
    {
        var rootType = typeof(TRoot);
        var routeDataProperty = rootType.GetProperty(RouteDataParameterName, BindingFlags.Public | BindingFlags.Instance);
        if (routeDataProperty is null || routeDataProperty.PropertyType != typeof(RouteData))
        {
            throw new InvalidOperationException(
                $"Root component '{rootType.FullName}' must declare a public '{RouteDataParameterName}' parameter of type '{nameof(RouteData)}'.");
        }

        _rootComponentType = rootType;
        _pageAssembly = rootType.Assembly;
        return this;
    }

    /// <summary>
    /// Marks a page component as the not-found page. It is generated as <c>404.html</c>
    /// and excluded from the sitemap.
    /// </summary>
    public KijiApp MapNotFound<TComponent>()
        where TComponent : IComponent
    {
        _notFoundComponentType = typeof(TComponent);
        return this;
    }

    /// <summary>
    /// Maps every item of a content collection to a page rendered by <typeparamref name="TPage"/>.
    /// The route values object's property names must match the page's route parameters.
    /// When the collection has a key, each page is associated with its content item,
    /// which enables <see cref="MapFeed{TContent}"/> route resolution.
    /// </summary>
    public KijiApp MapContent<TPage, TContent>(ContentCollection<TContent> collection, Func<TContent, object> routeValues)
        where TPage : IComponent
        where TContent : class
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(routeValues);

        _routeRegistrations.Add(new RouteRegistration(
            typeof(TPage),
            () => [.. collection.Items.Select(item => new StaticPageRouteEntry(
                RouteValues.ToDictionary(routeValues(item)),
                AssociatedContentIdentity: collection.HasKey ? collection.GetKeyFor(item) : null))]));
        return this;
    }

    /// <summary>
    /// Maps arbitrary dynamic routes to a page rendered by <typeparamref name="TPage"/>.
    /// The factory is re-evaluated for every site snapshot.
    /// </summary>
    public KijiApp MapRoutes<TPage>(Func<IEnumerable<object>> routeValues, bool excludeFromSitemap = false)
        where TPage : IComponent
    {
        ArgumentNullException.ThrowIfNull(routeValues);

        _routeRegistrations.Add(new RouteRegistration(
            typeof(TPage),
            () => [.. routeValues().Select(values => new StaticPageRouteEntry(
                RouteValues.ToDictionary(values),
                ExcludeFromSitemap: excludeFromSitemap))]));
        return this;
    }

    /// <summary>
    /// Generates an RSS feed from a keyed content collection. Each item's route is resolved
    /// from its <see cref="MapContent{TPage, TContent}"/> association; items without a mapped
    /// page are skipped. Entries appear in collection order.
    /// </summary>
    public KijiApp MapFeed<TContent>(ContentCollection<TContent> collection, Func<TContent, FeedItem> item)
        where TContent : class
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(item);

        _feedRegistrations.Add(routesByIdentity =>
        {
            var entries = new List<FeedEntry>();
            foreach (var content in collection.Items)
            {
                var identity = collection.GetKeyFor(content);
                if (!routesByIdentity.TryGetValue(identity, out var routePath))
                {
                    continue;
                }

                var feedItem = item(content);
                entries.Add(new FeedEntry(identity, feedItem.Title, feedItem.Description, feedItem.PublishedAt, routePath));
            }

            return entries;
        });
        return this;
    }

    /// <summary>
    /// Dispatches the command line: <c>build</c> (default), <c>clean</c>, <c>serve</c>, or <c>preview</c>.
    /// </summary>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void HandleShutdownSignal(PosixSignalContext context)
        {
            context.Cancel = true;
            cts.Cancel();
        }

        using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, HandleShutdownSignal);
        using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, HandleShutdownSignal);

        var args = _builder.Args;
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "build";

        try
        {
            switch (command)
            {
                case "build":
                    await BuildSiteAsync(
                        GetOptionValue(args, "--output"),
                        clean: !HasFlag(args, "--no-clean"),
                        cts.Token);
                    return 0;

                case "clean":
                    CleanOutput();
                    return 0;

                case "serve":
                    await ServeAsync(ParsePort(args), cts.Token);
                    return 0;

                case "preview":
                    await PreviewAsync(ParsePort(args), cts.Token);
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown command '{command}'.");
                    Console.Error.WriteLine("Usage: [build [--output <path>] [--no-clean]] | clean | serve [--port <n>] | preview [--port <n>]");
                    return 1;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return 1;
        }
    }

    /// <summary>
    /// Generates the full static site into the output directory.
    /// </summary>
    /// <param name="outputOverride">Optional output directory override.</param>
    /// <param name="clean">Whether to delete the output directory before generating.</param>
    public async Task BuildSiteAsync(string? outputOverride = null, bool clean = true, CancellationToken cancellationToken = default)
    {
        var options = _builder.Paths.ResolveForBuild(outputOverride);

        if (clean && Directory.Exists(options.OutputPath))
        {
            Directory.Delete(options.OutputPath, recursive: true);
        }

        Directory.CreateDirectory(options.OutputPath);

        var snapshot = CreateSnapshot();
        var renderer = GetRenderer();

        await StaticSiteGenerator.GenerateAsync(
            options,
            Site.BaseUrl,
            snapshot.Pages,
            (request, ct) => RenderPageAsync(renderer, request, ct),
            cancellationToken);

        await GenerateFeedsAsync(options, snapshot);
    }

    /// <summary>
    /// Starts the on-demand development server. Pages render per request through the
    /// same pipeline as <see cref="BuildSiteAsync"/>, content changes reload the browser
    /// automatically, and optimized images are cached under <c>.kiji-cache</c>.
    /// </summary>
    public async Task ServeAsync(int port = 8080, CancellationToken cancellationToken = default)
    {
        var (devServer, web) = await StartDevServerAsync(port, cancellationToken);
        await using (devServer)
        {
            Console.WriteLine($"Kiji dev server: {web.Urls.First()}");
            await web.WaitForShutdownAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Serves the generated output directory as static files, mirroring production
    /// trailing-slash and 404 handling. Run <c>build</c> first.
    /// </summary>
    public async Task PreviewAsync(int port = 8080, CancellationToken cancellationToken = default)
    {
        var outputPath = _builder.Paths.ResolveOutputPath();
        if (!Directory.Exists(outputPath))
        {
            throw new DirectoryNotFoundException(
                $"Output directory not found: {outputPath}. Run the 'build' command first.");
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        await using var web = builder.Build();

        web.Use(static async (context, next) =>
        {
            // Mirror Cloudflare's auto-trailing-slash behavior.
            var path = context.Request.Path.Value ?? "/";
            if (!path.EndsWith('/') && !Path.HasExtension(path))
            {
                context.Response.Redirect(path + "/" + context.Request.QueryString, permanent: true);
                context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
                return;
            }

            await next(context);
        });

        var fileProvider = new PhysicalFileProvider(outputPath);
        web.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        web.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider, ServeUnknownFileTypes = true });

        // Terminal 404 handler; endpoint routing is intentionally unused so the
        // static-file middleware handles every path (including extensionless ones).
        web.Run(async context =>
        {
            var notFoundPath = Path.Combine(outputPath, "404.html");
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            if (File.Exists(notFoundPath))
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(notFoundPath, context.RequestAborted);
            }
        });

        await web.StartAsync(cancellationToken);
        Console.WriteLine($"Kiji preview server: {web.Urls.First()}");
        await web.WaitForShutdownAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_renderer is not null)
        {
            await _renderer.DisposeAsync();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
    }

    internal async Task<(DevServer DevServer, WebApplication WebApplication)> StartDevServerAsync(
        int port,
        CancellationToken cancellationToken)
    {
        _serveOptions = _builder.Paths.ResolveForServe();
        EnsureServices();

        var devServer = new DevServer(this);
        var web = await devServer.StartAsync(_serveOptions, port, cancellationToken);
        return (devServer, web);
    }

    internal Task<string> RenderPageAsync(PageRenderRequest request, CancellationToken cancellationToken)
    {
        return RenderPageAsync(GetRenderer(), request, cancellationToken);
    }

    internal void InvalidateContent()
    {
        _builder.Runtime.Invalidate();
    }

    internal string? BuildFeedXml(SiteSnapshot snapshot)
    {
        if (_feedRegistrations.Count == 0)
        {
            return null;
        }

        var routesByIdentity = CreateRoutesByIdentity(snapshot);
        return FeedGenerator.BuildXml(
            _feedRegistrations[0](routesByIdentity),
            Site.BaseUrl,
            Site.Name,
            Site.Description,
            Site.Language);
    }

    internal string BuildSitemapXml(SiteSnapshot snapshot)
    {
        var urls = snapshot.Pages
            .Where(static page => !page.ExcludeFromSitemap)
            .Select(static page => page.RoutePath)
            .OrderBy(static route => route, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return SitemapGenerator.BuildXml(Site.BaseUrl, urls);
    }

    internal SiteSnapshot CreateSnapshot()
    {
        EnsureServices();

        var pageAssembly = _pageAssembly
            ?? throw new InvalidOperationException("No pages are mapped. Call MapPages<TRoot>() first.");

        var discovered = PageDiscovery.LoadDiscoveredPages(pageAssembly);
        discovered = ApplyNotFoundOverride(discovered);

        var pagesByComponent = new Dictionary<string, PageDiscovery.DiscoveredPage>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in discovered)
        {
            pagesByComponent.Add(page.SourceIdentifier, page);
        }

        var dynamicRoutes = new Dictionary<string, IReadOnlyList<StaticPageRouteEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in _routeRegistrations)
        {
            var page = FindDynamicPage(discovered, registration.ComponentType);
            var entries = registration.CreateEntries();
            ValidateRouteEntries(page, entries);

            dynamicRoutes[page.SourceIdentifier] = dynamicRoutes.TryGetValue(page.SourceIdentifier, out var existing)
                ? [.. existing, .. entries]
                : entries;
        }

        var plannedPages = StaticPagePlanner.PlanPages(
            [.. discovered.Select(static page => page.PageDefinition)],
            dynamicRoutes);

        var requests = plannedPages
            .Select(planned => CreatePageRenderRequest(pagesByComponent[planned.SourceIdentifier], planned))
            .ToList();

        return new SiteSnapshot(requests);
    }

    private static PageRenderRequest CreatePageRenderRequest(PageDiscovery.DiscoveredPage page, PlannedPage planned)
    {
        var parameters = planned.Parameters
            .ToDictionary(static pair => pair.Key, static pair => (object?)pair.Value, StringComparer.Ordinal);

        return new PageRenderRequest(
            planned.SourceIdentifier,
            page.ComponentType,
            parameters,
            planned.RoutePath,
            planned.OutputRelativePath,
            AssociatedContentIdentity: planned.AssociatedContentIdentity,
            ExcludeFromSitemap: planned.ExcludeFromSitemap);
    }

    private static PageDiscovery.DiscoveredPage FindDynamicPage(
        IReadOnlyList<PageDiscovery.DiscoveredPage> discovered,
        Type componentType)
    {
        var pages = discovered
            .Where(page => page.ComponentType == componentType && page.PageDefinition.IsDynamic)
            .ToArray();

        return pages switch
        {
            [var single] => single,
            [] => throw new InvalidOperationException(
                $"Component '{componentType.FullName}' has no dynamic '@page' route template to map routes onto."),
            _ => throw new InvalidOperationException(
                $"Component '{componentType.FullName}' declares multiple dynamic route templates; this is not supported."),
        };
    }

    private static void ValidateRouteEntries(
        PageDiscovery.DiscoveredPage page,
        IReadOnlyList<StaticPageRouteEntry> entries)
    {
        var expectedParameterNames = page.PageDefinition.ParameterNames.ToHashSet(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var routeKeys = entry.RouteValues.Keys.ToHashSet(StringComparer.Ordinal);
            var missing = expectedParameterNames
                .Where(name => !routeKeys.Contains(name))
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Route mapping for '{page.ComponentType.FullName}' did not supply required route values for '{page.SourceIdentifier}': {string.Join(", ", missing.Select(static name => $"'{name}'"))}.");
            }

            var extra = routeKeys
                .Where(name => !expectedParameterNames.Contains(name))
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
            if (extra.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Route mapping for '{page.ComponentType.FullName}' supplied route values not declared by '{page.SourceIdentifier}': {string.Join(", ", extra.Select(static name => $"'{name}'"))}.");
            }
        }
    }

    private IReadOnlyList<PageDiscovery.DiscoveredPage> ApplyNotFoundOverride(
        IReadOnlyList<PageDiscovery.DiscoveredPage> discovered)
    {
        if (_notFoundComponentType is null)
        {
            return discovered;
        }

        var notFoundPage = discovered.SingleOrDefault(page => page.ComponentType == _notFoundComponentType)
            ?? throw new InvalidOperationException(
                $"Not-found component '{_notFoundComponentType.FullName}' does not declare a '@page' route template.");

        var overridden = StaticPageDefinition.Create(
            notFoundPage.PageDefinition.SourceIdentifier,
            routePathOverride: "/404.html",
            outputRelativePathOverride: "404.html",
            excludeFromSitemap: true);

        return [.. discovered.Select(page => page == notFoundPage
            ? new PageDiscovery.DiscoveredPage(page.SourceIdentifier, page.ComponentType, overridden)
            : page)];
    }

    private Task<string> RenderPageAsync(ComponentRenderer renderer, PageRenderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var routeData = new RouteData(request.ComponentType, request.Parameters);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [RouteDataParameterName] = routeData,
        };

        return renderer.RenderComponentAsync(
            _rootComponentType!,
            parameters,
            Site.BaseUrl.AppendRelativePath(request.RoutePath));
    }

    private static Dictionary<string, string> CreateRoutesByIdentity(SiteSnapshot snapshot)
    {
        return snapshot.Pages
            .Where(static request => request.AssociatedContentIdentity is not null)
            .ToDictionary(
                static request => request.AssociatedContentIdentity!,
                static request => request.RoutePath,
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task GenerateFeedsAsync(SsgOptions options, SiteSnapshot snapshot)
    {
        if (_feedRegistrations.Count == 0)
        {
            return;
        }

        var routesByIdentity = CreateRoutesByIdentity(snapshot);

        foreach (var feedRegistration in _feedRegistrations)
        {
            await FeedGenerator.GenerateAsync(
                options.OutputPath,
                feedRegistration(routesByIdentity),
                Site.BaseUrl,
                Site.Name,
                Site.Description,
                Site.Language);
        }
    }

    private void CleanOutput()
    {
        var outputPath = _builder.Paths.ResolveOutputPath();
        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, recursive: true);
            Console.WriteLine($"Removed: {outputPath}");
        }
    }

    private void EnsureServices()
    {
        if (_services is not null)
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddSingleton(Site);
        services.AddSingleton(_ => _serveOptions ?? _builder.Paths.ResolveForBuild());
        _builder.Runtime.ApplyRegistrations(services);

        foreach (var descriptor in _builder.Services)
        {
            services.Add(descriptor);
        }

        services.TryAddSingleton<IImageAssetProcessor, NullImageAssetProcessor>();

        _services = services.BuildServiceProvider();
        _builder.Runtime.Attach(_services);
    }

    private ComponentRenderer GetRenderer()
    {
        if (_renderer is not null)
        {
            return _renderer;
        }

        EnsureServices();

        _renderer = ComponentRenderer.Create(
            services =>
            {
                services.AddSingleton(Site);
                _builder.Runtime.ApplyRegistrations(services);
                foreach (var descriptor in _builder.Services)
                {
                    services.Add(descriptor);
                }
            },
            Site.BaseUrl);

        return _renderer;
    }

    private static int ParsePort(string[] args)
    {
        var value = GetOptionValue(args, "--port");
        if (value is null)
        {
            return 8080;
        }

        return int.TryParse(value, out var port) && port is >= 0 and <= 65535
            ? port
            : throw new ArgumentException($"Invalid port: '{value}'.");
    }

    private static string? GetOptionValue(string[] args, string name)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(string[] args, string name)
    {
        for (var i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record RouteRegistration(
        Type ComponentType,
        Func<IReadOnlyList<StaticPageRouteEntry>> CreateEntries);
}
