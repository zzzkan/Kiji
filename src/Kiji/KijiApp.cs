using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Kiji.Assets;
using Kiji.Components;
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
/// (<c>build</c>, <c>dev</c>, <c>preview</c>, <c>clean</c>) with <see cref="RunAsync"/>.
/// </summary>
public sealed class KijiApp : IAsyncDisposable
{
    private readonly KijiBuilder _builder;
    private readonly List<RouteRegistration> _routeRegistrations = [];
    private readonly List<ISiteArtifact> _artifacts = [];
    private readonly List<Assembly> _pageAssemblies = [];
    private Type? _defaultLayoutType;
    private Type? _notFoundComponentType;
    private ServiceProvider? _services;
    private ComponentRenderer? _renderer;
    private SsgOptions? _activeOptions;
    private bool _forceFullBuild;

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
    /// The app's services. Deliberately not public: content must not be reachable while
    /// the site is still being declared, because loading it resolves <see cref="SsgOptions"/>
    /// — a singleton — and the running command is what decides those paths. Everything
    /// that legitimately needs services is handed a provider at a point where the command
    /// has already started: route and feed factories, content loaders, and
    /// <see cref="SiteOutputContext.Services"/> for artifacts.
    /// </summary>
    internal IServiceProvider Services
    {
        get
        {
            EnsureServices();
            return _services!;
        }
    }

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
    /// Registers the default layout applied to every page by the built-in root document.
    /// Individual pages can override it with the <c>@layout</c> directive. Optional:
    /// without a default layout, pages render directly inside <c>&lt;body&gt;</c>.
    /// </summary>
    public KijiApp MapDefaultLayout<TLayout>()
        where TLayout : LayoutComponentBase
    {
        _defaultLayoutType = typeof(TLayout);
        return this;
    }

    /// <summary>
    /// Registers every routable page component in the entry assembly: public,
    /// non-abstract components declaring a <c>@page</c> route template. This is the
    /// .NET equivalent of file-based routing — writing <c>@page</c> is what makes a
    /// component a page, wherever its file lives.
    /// </summary>
    public KijiApp MapPages()
    {
        var entryAssembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException(
                "No entry assembly is available in this host. Call MapPages(Assembly) with the assembly containing your pages.");

        return MapPages(entryAssembly);
    }

    /// <summary>
    /// Registers every routable page component in the given assembly: public,
    /// non-abstract components declaring a <c>@page</c> route template.
    /// May be called multiple times with different assemblies.
    /// </summary>
    public KijiApp MapPages(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (_pageAssemblies.Contains(assembly))
        {
            return this;
        }

        if (PageDiscovery.FromAssembly(assembly).Count == 0)
        {
            throw new InvalidOperationException(
                $"Assembly '{assembly.GetName().Name}' contains no routable page components. Pages are public, non-abstract components declaring a '@page' route template.");
        }

        _pageAssemblies.Add(assembly);
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
    /// Supplies the parameter sets for a page declaring a dynamic route template: one
    /// generated page per object returned. Each object's property names are the page's
    /// <c>[Parameter]</c> names — those that also appear in the route template bind the
    /// URL, and the rest are passed through to the component. The factory is
    /// re-evaluated for every site snapshot.
    /// </summary>
    /// <remarks>
    /// This says nothing about content. The factory receives the app's services, so
    /// project content by resolving a <see cref="ContentDictionary{T}"/> here — which is
    /// also the earliest point content can be read at all, since the running command has
    /// settled the site's paths by then.
    /// </remarks>
    public KijiApp MapRoutes<TPage>(Func<IServiceProvider, IEnumerable<object>> parameters)
        where TPage : IComponent
    {
        ArgumentNullException.ThrowIfNull(parameters);

        _routeRegistrations.Add(new RouteRegistration(
            typeof(TPage),
            () => [.. parameters(Services).Select(static values => new StaticPageRouteEntry(
                RouteValues.ToDictionary(values)))]));
        return this;
    }

    /// <summary>
    /// Registers a site-wide output artifact (e.g. an RSS feed or a sitemap) generated
    /// after all pages are rendered. Extension packages build on this method.
    /// </summary>
    public KijiApp MapArtifact(ISiteArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        _artifacts.Add(artifact);
        return this;
    }

    /// <summary>
    /// Dispatches the command line: <c>build</c> (default), <c>dev</c>, <c>preview</c>, or <c>clean</c>.
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

        var command = KijiCommandLine.Parse(_builder.Args);
        BuildOutput.Verbose = command.Verbose;
        _forceFullBuild = command.Force;

        try
        {
            switch (command.Kind)
            {
                case KijiCommandKind.Build:
                    await BuildSiteAsync(cts.Token);
                    return 0;

                case KijiCommandKind.Dev:
                    await DevAsync(command.Port, cts.Token);
                    return 0;

                case KijiCommandKind.Preview:
                    await PreviewAsync(command.Port, cts.Token);
                    return 0;

                case KijiCommandKind.Clean:
                    Clean();
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown command '{command.RawCommand}'.");
                    Console.Error.WriteLine(KijiCommandLine.Usage);
                    return 1;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Stopping a long-running server with Ctrl+C is a normal exit; an
            // interrupted build left partial output and is reported as failure.
            return command.Kind is KijiCommandKind.Dev or KijiCommandKind.Preview ? 0 : 1;
        }
    }

    /// <summary>
    /// Generates the static site into the output directory incrementally: pages whose
    /// inputs (content files, options, site assemblies) are unchanged since the last
    /// build are skipped and their outputs kept. Any ambiguity — no manifest, unknown
    /// files in the output directory, changed assemblies — falls back to a full
    /// rebuild. Run the <c>build</c> command with <c>--force</c> to always rebuild.
    /// </summary>
    public async Task BuildSiteAsync(CancellationToken cancellationToken = default)
    {
        var options = _builder.Paths.ResolveForBuild();
        UseOptions(options);

        var stopwatch = Stopwatch.StartNew();
        var snapshot = CreateSnapshot();
        var renderer = GetRenderer();

        StaticSiteGenerator.ValidateNoStaticFileCollisions(options, snapshot.Pages);

        var planner = new IncrementalBuildPlanner(
            options,
            _builder.Paths.Root,
            _builder.Paths.ResolveCachePath(),
            Site,
            _builder.BuildInputs,
            _services!.GetService<ContentFileHashRegistry>());

        var plan = await planner.CreatePlanAsync(
            snapshot.Pages,
            [.. _pageAssemblies, typeof(KijiApp).Assembly],
            _forceFullBuild,
            cancellationToken);

        if (plan.FullClean && Directory.Exists(options.OutputPath))
        {
            Directory.Delete(options.OutputPath, recursive: true);
        }

        Directory.CreateDirectory(options.OutputPath);

        var staticFiles = await planner.SyncStaticFilesAsync(plan);

        // Render only the pages the plan could not prove unchanged, recording what
        // each render reads and writes for the next build's skip checks.
        var recorders = new ConcurrentDictionary<string, BuildDependencyRecorder>(StringComparer.OrdinalIgnoreCase);
        var rendered = await StaticSiteGenerator.RenderPagesAsync(
            options,
            plan.PagesToRender,
            (request, output, ct) =>
            {
                var recorder = recorders.GetOrAdd(request.OutputRelativePath, static _ => new BuildDependencyRecorder());
                return RenderPageAsync(renderer, request, output, recorder, ct);
            },
            cancellationToken);

        var artifacts = await GenerateArtifactsAsync(options, snapshot, cancellationToken);

        var pageEntries = new List<BuildManifestPage>(plan.CarriedPages.Count + rendered.Count);
        pageEntries.AddRange(plan.CarriedPages);
        foreach (var page in rendered)
        {
            pageEntries.Add(planner.CreatePageEntry(
                page.Request,
                page.OutputHash,
                recorders[page.Request.OutputRelativePath]));
        }

        var manifest = new BuildManifest
        {
            SchemaVersion = BuildManifest.CurrentSchemaVersion,
            OptionsHash = plan.OptionsHash,
            AssemblyMvids = plan.AssemblyMvids,
            Pages = pageEntries,
            StaticFiles = staticFiles,
            Artifacts = artifacts,
        };

        planner.RemoveOrphans(plan.OldManifest, manifest);
        await planner.SaveManifestAsync(manifest, cancellationToken);

        BuildOutput.Info(
            $"Generated {rendered.Count} pages ({plan.CarriedPages.Count} unchanged, skipped) in {stopwatch.ElapsedMilliseconds} ms.");
    }

    /// <summary>
    /// Starts the on-demand development server. Pages render per request through the
    /// same pipeline as <see cref="BuildSiteAsync"/>, content changes reload the browser
    /// automatically, and optimized images are cached under <c>.kiji/cache</c>.
    /// </summary>
    public async Task DevAsync(int port = 8080, CancellationToken cancellationToken = default)
    {
        var (devServer, web) = await StartDevServerAsync(port, cancellationToken);
        await using (devServer)
        {
            await web.WaitForShutdownAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Serves the generated output directory as static files, mirroring production
    /// trailing-slash and 404 handling. Run <c>build</c> first.
    /// </summary>
    public async Task PreviewAsync(int port = 8080, CancellationToken cancellationToken = default)
    {
        await using var web = await StartPreviewServerAsync(port, cancellationToken);
        await web.WaitForShutdownAsync(cancellationToken);
    }

    internal async Task<WebApplication> StartPreviewServerAsync(int port, CancellationToken cancellationToken)
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

        var web = builder.Build();

        var fileProvider = new PhysicalFileProvider(outputPath);

        // Mount under the site's base path first, so every downstream middleware sees
        // prefix-stripped paths and production-equivalent 404s outside it.
        web.UseSiteBasePath(Site.BasePath);

        web.Use(async (context, next) =>
        {
            // Resolve /route and /route/ to the same page without redirecting,
            // matching common static host behavior for directory-style output.
            var path = context.Request.Path.Value ?? "/";
            if (!path.EndsWith('/')
                && !fileProvider.GetFileInfo(path).Exists
                && fileProvider.GetFileInfo(path + "/index.html").Exists)
            {
                context.Request.Path = path + "/";
            }

            await next(context);
        });
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

        try
        {
            await web.StartAsync(cancellationToken);
        }
        catch
        {
            await web.DisposeAsync();
            throw;
        }

        Console.WriteLine($"Kiji preview server: {new Uri(new Uri(web.Urls.First()), Site.BasePath)}");
        return web;
    }

    /// <summary>
    /// Deletes the build outputs: the output directory (default <c>dist</c>) and the
    /// <c>.kiji</c> directory (build manifest, image cache, dev-server site mirror).
    /// The next build is a full rebuild.
    /// </summary>
    public void Clean()
    {
        DeleteRecursively(_builder.Paths.ResolveOutputPath());
        DeleteRecursively(_builder.Paths.ResolveKijiPath());
    }

    private static void DeleteRecursively(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
        BuildOutput.Info($"Removed: {path}");
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
        CancellationToken cancellationToken,
        DevServerStatusReporter? reporter = null)
    {
        var options = UseOptions(_builder.Paths.ResolveForServe());
        EnsureServices();

        var devServer = new DevServer(this, reporter);
        var web = await devServer.StartAsync(options, port, cancellationToken);
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

    internal SiteSnapshot CreateSnapshot()
    {
        // Planning expands route factories, which read content. If no command has
        // settled the options yet, this is a build.
        UseOptions(_builder.Paths.ResolveForBuild());
        EnsureServices();

        if (_pageAssemblies.Count == 0)
        {
            throw new InvalidOperationException("No pages are mapped. Call MapPages(...) first.");
        }

        var scanned = new List<PageDiscovery.DiscoveredPage>();
        foreach (var assembly in _pageAssemblies)
        {
            scanned.AddRange(PageDiscovery.FromAssembly(assembly));
        }

        var discovered = PageDiscovery.EnsureUniqueRoutes(scanned);
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

    private PageRenderRequest CreatePageRenderRequest(PageDiscovery.DiscoveredPage page, PlannedPage planned)
    {
        var parameters = planned.Parameters
            .ToDictionary(static pair => pair.Key, static pair => (object?)pair.Value, StringComparer.Ordinal);

        var request = new PageRenderRequest(
            planned.SourceIdentifier,
            page.ComponentType,
            parameters,
            planned.RoutePath,
            planned.OutputRelativePath,
            ExcludeFromSitemap: planned.ExcludeFromSitemap);

        return request with { RootParameters = CreateRootParameters(request) };
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
        // Values naming a route-template parameter bind the URL and must be a single
        // usable segment. Everything else is an ordinary component parameter: it never
        // reaches the path, so segment rules do not apply — but it does have to name a
        // real [Parameter], or the component rejects it at render time with no hint of
        // which mapping produced it.
        var routeParameterNames = page.PageDefinition.ParameterNames.ToHashSet(StringComparer.Ordinal);
        var declaredParameterNames = PageDiscovery.ParameterNames(page.ComponentType);

        foreach (var entry in entries)
        {
            var suppliedNames = entry.RouteValues.Keys.ToHashSet(StringComparer.Ordinal);
            var missing = routeParameterNames
                .Where(name => !suppliedNames.Contains(name))
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Route mapping for '{page.ComponentType.FullName}' did not supply required route values for '{page.SourceIdentifier}': {string.Join(", ", missing.Select(static name => $"'{name}'"))}.");
            }

            foreach (var (name, value) in entry.RouteValues)
            {
                if (routeParameterNames.Contains(name))
                {
                    ValidateRouteValue(page, name, value);
                }
            }

            var undeclared = suppliedNames
                .Where(name => !routeParameterNames.Contains(name) && !declaredParameterNames.Contains(name))
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
            if (undeclared.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Route mapping for '{page.ComponentType.FullName}' ('{page.SourceIdentifier}') supplied values that are neither route parameters nor declared '[Parameter]' properties: {string.Join(", ", undeclared.Select(static name => $"'{name}'"))}.");
            }
        }
    }

    private static void ValidateRouteValue(PageDiscovery.DiscoveredPage page, string name, string value)
    {
        if (value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Route mapping for '{page.ComponentType.FullName}' supplied invalid route value '{name}' for '{page.SourceIdentifier}': '{value}'. Route values must be a single route segment and cannot contain '/' or '\\'.");
        }

        var trimmed = value.Trim();
        if (trimmed is "." or "..")
        {
            throw new InvalidOperationException(
                $"Route mapping for '{page.ComponentType.FullName}' supplied invalid route value '{name}' for '{page.SourceIdentifier}': '{value}'. Route values cannot be '.' or '..'.");
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

    private async Task<string> RenderPageAsync(ComponentRenderer renderer, PageRenderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PageRenderContext.SetCurrent(CreatePageRenderContext(request));
        try
        {
            return await renderer.RenderComponentAsync<KijiRoot>(
                request.RootParameters ?? CreateRootParameters(request),
                Site.BaseUrl.AppendRelativePath(request.RoutePath));
        }
        finally
        {
            PageRenderContext.SetCurrent(null);
        }
    }

    private async Task RenderPageAsync(
        ComponentRenderer renderer,
        PageRenderRequest request,
        TextWriter output,
        BuildDependencyRecorder? dependencies,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PageRenderContext.SetCurrent(CreatePageRenderContext(request, dependencies));
        try
        {
            await renderer.RenderComponentToAsync<KijiRoot>(
                output,
                request.RootParameters ?? CreateRootParameters(request),
                Site.BaseUrl.AppendRelativePath(request.RoutePath));
        }
        finally
        {
            PageRenderContext.SetCurrent(null);
        }
    }

    private static PageRenderContext CreatePageRenderContext(PageRenderRequest request, BuildDependencyRecorder? dependencies = null)
    {
        return new PageRenderContext
        {
            RoutePath = request.RoutePath,
            OutputRelativeDirectory = Path.GetDirectoryName(request.OutputRelativePath) ?? string.Empty,
            Dependencies = dependencies,
        };
    }

    private Dictionary<string, object?> CreateRootParameters(PageRenderRequest request)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [nameof(KijiRoot.PageType)] = request.ComponentType,
            [nameof(KijiRoot.PageParameters)] = request.Parameters,
            [nameof(KijiRoot.DefaultLayout)] = _defaultLayoutType,
        };
    }

    private SiteOutputContext CreateOutputContext(SiteSnapshot snapshot)
    {
        return new SiteOutputContext(
            Site,
            [.. snapshot.Pages.Select(static page => new SitePageInfo(
                page.RoutePath,
                page.OutputRelativePath,
                page.ExcludeFromSitemap))],
            Services);
    }

    private async Task<IReadOnlyList<string>> GenerateArtifactsAsync(SsgOptions options, SiteSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_artifacts.Count == 0)
        {
            return [];
        }

        var artifactRelativePaths = new List<string>(_artifacts.Count);
        var context = CreateOutputContext(snapshot);
        var reservedPaths = CreateReservedArtifactPaths(options, snapshot);

        foreach (var artifact in _artifacts)
        {
            var fullPath = ResolveArtifactPath(options.OutputPath, artifact.OutputRelativePath);
            if (reservedPaths.TryGetValue(fullPath, out var collisionTarget))
            {
                throw new InvalidOperationException(
                    $"Artifact output path '{artifact.OutputRelativePath}' collides with {collisionTarget}.");
            }

            reservedPaths[fullPath] = "another artifact output path";
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
            await artifact.WriteAsync(stream, context, cancellationToken);

            artifactRelativePaths.Add(Path.GetRelativePath(options.OutputPath, fullPath));
            BuildOutput.Info($"Generated: {fullPath}");
        }

        return artifactRelativePaths;
    }

    private static Dictionary<string, string> CreateReservedArtifactPaths(SsgOptions options, SiteSnapshot snapshot)
    {
        var reservedPaths = snapshot.Pages.ToDictionary(
            page => ResolveArtifactPath(options.OutputPath, page.OutputRelativePath),
            static _ => "a generated page output path",
            StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(options.StaticPath))
        {
            return reservedPaths;
        }

        foreach (var file in Directory.EnumerateFiles(options.StaticPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(options.StaticPath, file);
            var outputPath = ResolveArtifactPath(options.OutputPath, relativePath);
            reservedPaths.TryAdd(outputPath, "a static file output path");
        }

        return reservedPaths;
    }

    private static string ResolveArtifactPath(string outputPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputPath));
        var fullPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath));
        if (!fullPath.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Artifact output path '{relativePath}' escapes the output directory.");
        }

        return fullPath;
    }

    /// <summary>
    /// Settles the paths the running command works against. First caller wins:
    /// <see cref="SsgOptions"/> is a singleton, so what is fixed here is what every
    /// loader and renderer sees for the rest of the process.
    /// </summary>
    private SsgOptions UseOptions(SsgOptions options)
    {
        _activeOptions ??= options;
        return _activeOptions;
    }

    /// <summary>
    /// Settles on build paths without running a build. For tests that load content
    /// directly; a real site reaches this through one of the commands.
    /// </summary>
    internal void UseBuildOptions()
    {
        UseOptions(_builder.Paths.ResolveForBuild());
    }

    private void EnsureServices()
    {
        if (_services is not null)
        {
            return;
        }

        var services = new ServiceCollection();
        ComponentRenderer.AddComponentRenderingServices(services);
        services.AddSingleton(Site);
        // Deliberately not defaulted: the command decides these paths (build writes to
        // the output directory, dev to its own mirror) and, as a singleton, the first
        // resolution wins for the whole process. No public API hands out a provider
        // before a command starts, so this is an invariant rather than a user-facing
        // error — but a default here would silently pin build paths onto dev.
        services.AddSingleton(_ => _activeOptions ?? throw new InvalidOperationException(
            "SsgOptions was resolved before a command settled the site's paths."));
        _builder.Runtime.ApplyRegistrations(services);

        foreach (var descriptor in _builder.Services)
        {
            services.Add(descriptor);
        }

        services.TryAddSingleton<IImageAssetProcessor>(static _ => new ImageProcessor());
        services.AddSingleton<ContentFileHashRegistry>();

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

        // The renderer shares the single app container, so components see the exact
        // same registrations (options, content collections, image backend) as loaders.
        _renderer = ComponentRenderer.Attach(_services!, Site.BaseUrl);
        return _renderer;
    }

    private sealed record RouteRegistration(
        Type ComponentType,
        Func<IReadOnlyList<StaticPageRouteEntry>> CreateEntries);
}
