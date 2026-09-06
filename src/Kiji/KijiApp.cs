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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Kiji;

/// <summary>
/// A Kiji static site application. Created via <see cref="KijiBuilder.Build"/>;
/// declare page mappings with the <c>Map*</c> methods, then hand control to
/// <see cref="RunAsync(CancellationToken)"/>.
///
/// <para>
/// There are no commands. <c>dotnet run</c> and <c>dotnet watch</c> start the dev server;
/// <c>dotnet publish</c> generates the site, because Kiji's MSBuild targets run this same
/// app with <c>KIJI_OUTPUT</c> set to the publish directory.
/// </para>
/// </summary>
public sealed class KijiApp : IAsyncDisposable
{
    private readonly KijiBuilder _builder;
    private readonly List<RouteRegistration> _routeRegistrations = [];
    private readonly List<ISiteArtifact> _artifacts = [];
    private readonly List<Assembly> _pageAssemblies = [];
    private Type? _defaultLayoutType;
    private Type? _notFoundComponentType;
    /// <summary>Where to generate the site. Set by Kiji's MSBuild targets on publish.</summary>
    private const string OutputPathVariable = "KIJI_OUTPUT";

    /// <summary>Skip the incremental plan and rebuild everything. <c>-p:KijiForce=true</c>.</summary>
    private const string ForceVariable = "KIJI_FORCE";

    /// <summary>Report per-file output. <c>-p:KijiVerbose=true</c>.</summary>
    private const string VerboseVariable = "KIJI_VERBOSE";

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
    /// <param name="args">Command line arguments; forwarded to <see cref="RunAsync(CancellationToken)"/> for command dispatch.</param>
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
    /// Runs the site: generates it when the build host asked for that, otherwise starts
    /// the dev server.
    /// </summary>
    /// <remarks>
    /// The only signal is <c>KIJI_OUTPUT</c>, the directory to generate into. Kiji's
    /// MSBuild targets set it during <c>dotnet publish</c>; nothing sets it under
    /// <c>dotnet run</c> or <c>dotnet watch</c>, so those get the dev server. The value
    /// doubles as the mode, which is why there is no separate command to name.
    /// <para>
    /// A site whose entry point never reaches this method generates nothing on publish —
    /// this is the only place <c>KIJI_OUTPUT</c> is read.
    /// </para>
    /// </remarks>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(Environment.GetEnvironmentVariable, cancellationToken);
    }

    /// <param name="environment">
    /// Reads an environment variable. Injected so tests can exercise both modes without
    /// mutating the process environment, which parallel test classes share.
    /// </param>
    /// <param name="cancellationToken">Stops the dev server or the generation.</param>
    /// <inheritdoc cref="RunAsync(CancellationToken)"/>
    internal async Task<int> RunAsync(Func<string, string?> environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void HandleShutdownSignal(PosixSignalContext context)
        {
            context.Cancel = true;
            cts.Cancel();
        }

        using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, HandleShutdownSignal);
        using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, HandleShutdownSignal);

        var outputPath = environment(OutputPathVariable);
        var publishing = !string.IsNullOrWhiteSpace(outputPath);

        try
        {
            if (publishing)
            {
                BuildOutput.Verbose = EnvironmentValue.IsTruthy(environment(VerboseVariable));
                _forceFullBuild = EnvironmentValue.IsTruthy(environment(ForceVariable));
                await PublishSiteAsync(outputPath!, cts.Token);
                return 0;
            }

            await DevAsync(cts.Token);
            return 0;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Stopping the dev server with Ctrl+C is a normal exit; an interrupted
            // generation left partial output and is reported as failure.
            return publishing ? 1 : 0;
        }
    }

    /// <summary>
    /// Generates the static site into <paramref name="outputPath"/> incrementally: pages
    /// whose inputs (content files, options, site assemblies) are unchanged since the last
    /// run are skipped and their outputs kept. Any ambiguity — no manifest, unknown files
    /// in the output directory, changed assemblies — falls back to a full rebuild.
    /// Publish with <c>-p:KijiForce=true</c> to always rebuild.
    /// </summary>
    /// <param name="outputPath">
    /// Where to write the site. <c>dotnet publish</c> supplies this through
    /// <c>KIJI_OUTPUT</c>; there is deliberately no default, because the publish
    /// directory is the only thing that knows it.
    /// </param>
    /// <param name="cancellationToken">Cancels the generation.</param>
    public async Task PublishSiteAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var options = _builder.Paths.ResolveForPublish(outputPath);
        UseOptions(options);

        var stopwatch = Stopwatch.StartNew();
        var phases = new BuildPhaseTimer();
        var snapshot = CreateSnapshot();
        var renderer = GetRenderer();

        StaticSiteGenerator.ValidateNoStaticFileCollisions(options, snapshot.Pages);
        phases.Mark(BuildPhaseTimer.Snapshot);

        var planner = new IncrementalBuildPlanner(
            options,
            _builder.Paths.Root,
            _builder.Paths.ResolveCachePath(),
            Site,
            _builder.BuildInputs,
            _services!.GetService<ContentFileRegistry>());

        var plan = await planner.CreatePlanAsync(
            snapshot.Pages,
            [.. _pageAssemblies, typeof(KijiApp).Assembly],
            _forceFullBuild,
            cancellationToken);
        phases.Mark(BuildPhaseTimer.Plan);

        // The output directory is kept, never wiped: whatever this build does not
        // produce is deleted by the reconciliation pass below, which is both cheaper
        // than a delete-and-rewrite and checked against the directory itself.
        Directory.CreateDirectory(options.OutputPath);
        phases.Mark(BuildPhaseTimer.Clean);

        var staticFiles = await planner.SyncStaticFilesAsync(plan);
        phases.Mark(BuildPhaseTimer.Static);

        // Render only the pages the plan could not prove unchanged, recording what
        // each render reads and writes for the next build's skip checks. The previous
        // manifest comes along so a render that reproduces the bytes already on disk
        // skips the write; with --force there is no manifest and everything is written.
        var recorders = new ConcurrentDictionary<string, BuildDependencyRecorder>(StringComparer.OrdinalIgnoreCase);
        var rendered = await StaticSiteGenerator.RenderPagesAsync(
            options,
            plan.PagesToRender,
            (request, output, ct) =>
            {
                var recorder = recorders.GetOrAdd(request.OutputRelativePath, static _ => new BuildDependencyRecorder());
                return RenderPageAsync(renderer, request, output, recorder, ct);
            },
            plan.OldManifest?.Pages.ToDictionary(
                static page => page.OutputRelativePath,
                StringComparer.OrdinalIgnoreCase),
            cancellationToken);
        phases.Mark(BuildPhaseTimer.Render);

        var artifacts = await GenerateArtifactsAsync(options, snapshot, cancellationToken);
        phases.Mark(BuildPhaseTimer.Artifacts);

        // One entry per rendered page, each stat-ing its own dependencies: independent
        // work, so it runs in parallel and lands at its own index to keep the manifest
        // order deterministic.
        var renderedEntries = new BuildManifestPage[rendered.Count];
        Parallel.For(0, rendered.Count, index =>
        {
            var page = rendered[index];
            renderedEntries[index] = planner.CreatePageEntry(
                page.Request,
                page.OutputHash,
                recorders[page.Request.OutputRelativePath]);
        });

        var pageEntries = new List<BuildManifestPage>(plan.CarriedPages.Count + rendered.Count);
        pageEntries.AddRange(plan.CarriedPages);
        pageEntries.AddRange(renderedEntries);

        phases.Mark(BuildPhaseTimer.Entries);

        var manifest = new BuildManifest
        {
            SchemaVersion = BuildManifest.CurrentSchemaVersion,
            OptionsHash = plan.OptionsHash,
            AssemblyMvids = plan.AssemblyMvids,
            Pages = pageEntries,
            StaticFiles = staticFiles,
            Artifacts = artifacts,
        };

        planner.ReconcileOutputs(manifest);
        phases.Mark(BuildPhaseTimer.Reconcile);

        await planner.SaveManifestAsync(manifest, cancellationToken);
        phases.Mark(BuildPhaseTimer.Manifest);

        var written = rendered.Count(static page => page.Written);
        var reproduced = rendered.Count - written;
        var reproducedNote = reproduced > 0 ? $", {reproduced} re-rendered but unchanged" : string.Empty;
        BuildOutput.Info(
            $"Generated {written} pages ({plan.CarriedPages.Count} unchanged, skipped{reproducedNote}) in {stopwatch.ElapsedMilliseconds} ms.");
    }

    /// <summary>
    /// Starts the on-demand development server. Pages render per request through the
    /// same pipeline as <see cref="PublishSiteAsync"/>, content changes reload the browser
    /// automatically, and optimized images are cached under <c>.kiji/cache</c>. Nothing
    /// is written to a publish directory.
    /// </summary>
    /// <remarks>
    /// The address comes from ASP.NET Core configuration — <c>ASPNETCORE_URLS</c>,
    /// <c>--urls</c>, or <c>launchSettings.json</c> — and falls back to
    /// <c>http://127.0.0.1:8080</c> when none of those set one.
    /// </remarks>
    public async Task DevAsync(CancellationToken cancellationToken = default)
    {
        var (devServer, web) = await StartDevServerAsync(cancellationToken);
        await using (devServer)
        {
            await web.WaitForShutdownAsync(cancellationToken);
        }
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

    internal Task<(DevServer DevServer, WebApplication WebApplication)> StartDevServerAsync(
        CancellationToken cancellationToken)
    {
        return StartDevServerAsync(_builder.Args, cancellationToken);
    }

    /// <param name="args">
    /// Forwarded to <c>WebApplication.CreateSlimBuilder</c>, so <c>--urls</c> works the
    /// way it does for any ASP.NET Core app. Tests pass an explicit <c>--urls</c> with
    /// port 0 rather than mutating process-wide state.
    /// </param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <param name="reporter">Overrides the console reporter. For tests.</param>
    internal async Task<(DevServer DevServer, WebApplication WebApplication)> StartDevServerAsync(
        string[] args,
        CancellationToken cancellationToken,
        DevServerStatusReporter? reporter = null)
    {
        var options = UseOptions(_builder.Paths.ResolveForServe());
        EnsureServices();

        var devServer = new DevServer(this, reporter);
        var web = await devServer.StartAsync(options, args, cancellationToken);
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
        // Planning expands route factories, which read content. If nothing has settled
        // the options yet, fall back to paths that cannot be mistaken for a deliverable.
        UseOptions(_builder.Paths.ResolveForPlanning());
        EnsureServices();

        if (_pageAssemblies.Count == 0)
        {
            throw new InvalidOperationException("No pages are mapped. Call MapPages(...) first.");
        }

        var snapshotPhases = new BuildPhaseTimer();
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

        snapshotPhases.Mark(BuildPhaseTimer.Discovery);

        var dynamicRoutes = new Dictionary<string, IReadOnlyList<StaticPageRouteEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in _routeRegistrations)
        {
            var page = FindDynamicPage(discovered, registration.ComponentType);
            var entries = registration.CreateEntries();
            snapshotPhases.Mark(BuildPhaseTimer.RouteEntries);
            ValidateRouteEntries(page, entries);
            snapshotPhases.Mark(BuildPhaseTimer.RouteValidation);

            dynamicRoutes[page.SourceIdentifier] = dynamicRoutes.TryGetValue(page.SourceIdentifier, out var existing)
                ? [.. existing, .. entries]
                : entries;
        }

        // Expanding a route factory is where content is first read, so this is where
        // loading and parsing every markdown file lands.
        snapshotPhases.Mark(BuildPhaseTimer.Routes);

        var plannedPages = StaticPagePlanner.PlanPages(
            [.. discovered.Select(static page => page.PageDefinition)],
            dynamicRoutes);

        var requests = plannedPages
            .Select(planned => CreatePageRenderRequest(pagesByComponent[planned.SourceIdentifier], planned))
            .ToList();

        snapshotPhases.Mark(BuildPhaseTimer.Planning);

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
    /// Settles on planning paths without generating anything. For tests that load content
    /// directly; a real site reaches this through <see cref="RunAsync(CancellationToken)"/>.
    /// </summary>
    internal void UsePlanningOptions()
    {
        UseOptions(_builder.Paths.ResolveForPlanning());
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
        // Deliberately not defaulted: the run decides these paths (a publish writes to
        // the directory dotnet publish chose, the dev server to its own mirror) and, as a
        // singleton, the first resolution wins for the whole process. No public API hands
        // out a provider before the run starts, so this is an invariant rather than a
        // user-facing error — but a default here would silently pin publish paths onto
        // the dev server.
        services.AddSingleton(_ => _activeOptions ?? throw new InvalidOperationException(
            "SsgOptions was resolved before a command settled the site's paths."));
        _builder.Runtime.ApplyRegistrations(services);

        foreach (var descriptor in _builder.Services)
        {
            services.Add(descriptor);
        }

        services.TryAddSingleton<IImageAssetProcessor>(static _ => new ImageProcessor());
        services.AddSingleton<ContentFileRegistry>();

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
