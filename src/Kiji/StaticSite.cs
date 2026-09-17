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
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kiji;

/// <summary>Defines, generates, and serves a static website.</summary>
/// <remarks>Configure and register content before execution; settings become read-only when execution starts.</remarks>
public sealed class StaticSite
{
    private readonly string[] _args;
    private readonly ContentRuntime _runtime = new();
    private readonly List<string> _buildInputPaths = [];
    private readonly List<KeyValuePair<string, string>> _buildInputValues = [];
    private readonly HashSet<Type> _pageServiceTypes = [];
    private Func<IImageProcessor> _imageProcessorFactory = static () => new ImageProcessor();
    private bool _configurationFrozen;
    private readonly List<RouteRegistration> _routeRegistrations = [];
    private readonly List<SiteArtifactRegistration> _artifacts = [];
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
    private ResolvedSitePaths? _activeOptions;
    private bool _forceFullBuild;
    private bool _hasRun;
    private bool _disposed;

    private StaticSite(string[] args)
    {
        _args = [.. args];
        Paths = new SitePaths(SitePaths.ResolveDefaultRoot(AppContext.BaseDirectory, Directory.GetCurrentDirectory()));
    }

    /// <summary>The site metadata, required before execution and readable only after assignment.</summary>
    public SiteInfo Info
    {
        get => field ?? throw new InvalidOperationException("Set StaticSite.Info before running the site.");
        set
        {
            EnsureConfigurable();
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = null!;

    /// <summary>The site directories, configurable before execution starts.</summary>
    public SitePaths Paths { get; }

    /// <summary>Registers a concrete service shared by components within one page render.</summary>
    /// <remarks>
    /// Constructor dependencies are resolved automatically. Each render gets a fresh instance,
    /// disposed when that render finishes. Content loaders, route/feed factories and artifacts
    /// cannot resolve page services. Registration does not cache method results.
    /// </remarks>
    public StaticSite AddPageService<T>() where T : class
    {
        EnsureConfigurable();
        var type = typeof(T);
        if (type.IsAbstract || type.IsInterface || type.GetConstructors().Length == 0)
        {
            throw new ArgumentException($"Page service '{type.FullName}' must be a concrete type with a public constructor.");
        }

        if (!_pageServiceTypes.Add(type))
        {
            throw new InvalidOperationException($"Page service '{type.FullName}' is already registered.");
        }

        return this;
    }

    /// <summary>Replaces the image processor with a lazily created, site-owned instance.</summary>
    /// <remarks>
    /// The last registration wins. The factory must return a new, non-null instance; Kiji
    /// disposes it with the site. The processor must support concurrent calls and must not
    /// retain page or content state. Content changes and hot reload do not recreate it.
    /// Declare external configuration with AddBuildInput and include encoder settings in
    /// any custom persistent cache identity.
    /// </remarks>
    public StaticSite UseImageProcessor(Func<IImageProcessor> factory)
    {
        EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(factory);
        _imageProcessorFactory = factory;
        return this;
    }

    internal IEnumerable<string> WatchedBuildInputs => _buildInputPaths
        .Select(path => Path.GetFullPath(path, Path.GetFullPath(Paths.RootDirectory)));

    /// <summary>
    /// The app's services. Deliberately not public: content must not be reachable while
    /// the site is still being declared, because loading it resolves <see cref="ResolvedSitePaths"/>
    /// — a singleton — and the running command is what decides those paths. Everything
    /// that legitimately needs services is handed a provider at a point where the command
    /// has already started: route and feed factories, content loaders, and
    /// <see cref="SiteOutputContext.Services"/> for artifacts.
    /// </summary>
    internal IServiceProvider ServiceProvider
    {
        get
        {
            EnsureServices();
            return _services!;
        }
    }

    /// <summary>
    /// Creates a site for configuration, registration, and execution.
    /// </summary>
    /// <param name="args">Arguments forwarded to ASP.NET Core configuration when serving.</param>
    public static StaticSite Create(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return new StaticSite(args);
    }

    /// <summary>Registers a file or directory whose changes require a full rebuild.</summary>
    /// <param name="path">An absolute path or a path relative to <see cref="SitePaths.RootDirectory"/>.</param>
    public StaticSite AddBuildInput(string path)
    {
        EnsureConfigurable();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _buildInputPaths.Add(path);
        return this;
    }

    /// <summary>Registers a named value whose changes require a full rebuild.</summary>
    public StaticSite AddBuildInput(string key, string value)
    {
        EnsureConfigurable();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        _buildInputValues.Add(new KeyValuePair<string, string>(key, value));
        return this;
    }

    /// <summary>Uses a content source resolved as <see cref="ContentDictionary{T}"/>.</summary>
    /// <param name="loader">Loads items lazily once per snapshot, after execution paths are settled.</param>
    public StaticSite UseContentSource<T>(
        Func<IServiceProvider, IReadOnlyList<T>> loader)
        where T : class
    {
        EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(loader);

        return UseContentSource(
            services =>
            {
                var items = loader(services);
                var keyed = new (string Key, T Item, string? SourceFile)[items.Count];
                for (var i = 0; i < items.Count; i++)
                {
                    keyed[i] = (i.ToString(System.Globalization.CultureInfo.InvariantCulture), items[i], null);
                }
                return keyed;
            });
    }

    /// <summary>
    /// The provenance-carrying form used by file-backed content loaders.
    /// </summary>
    internal StaticSite UseContentSource<T>(
        Func<IServiceProvider, IReadOnlyList<(string Key, T Item, string? SourceFile)>> loader,
        string contentSetScope = "")
        where T : class
    {
        EnsureConfigurable();
        _runtime.Register(new ContentDictionary<T>(_runtime, loader, contentSetScope));
        return this;
    }

    /// <summary>Registers the default layout for pages without their own <c>@layout</c>.</summary>
    /// <remarks>Without a default layout, pages render directly inside the document body.</remarks>
    public StaticSite UseDefaultLayout<TLayout>()
        where TLayout : LayoutComponentBase
    {
        EnsureConfigurable();
        _defaultLayoutType = typeof(TLayout);
        return this;
    }

    /// <summary>Registers public, non-abstract pages with parameterless routes from the entry assembly.</summary>
    public StaticSite AddStaticPages()
    {
        EnsureConfigurable();
        var entryAssembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException(
                "No entry assembly is available in this host. Call AddStaticPages(Assembly) with the assembly containing your pages.");

        return AddStaticPages(entryAssembly);
    }

    /// <summary>Registers public, non-abstract pages with parameterless routes from an assembly.</summary>
    /// <remarks>Repeated registration of the same assembly has no effect; parameterized routes are ignored.</remarks>
    public StaticSite AddStaticPages(Assembly assembly)
    {
        EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(assembly);

        if (_pageAssemblies.Contains(assembly))
        {
            return this;
        }

        _pageAssemblies.Add(assembly);
        return this;
    }

    /// <summary>Registers a page as <c>404.html</c>.</summary>
    /// <remarks>The component must declare exactly one route and cannot also be registered with <c>AddPages</c>.</remarks>
    public StaticSite UseNotFoundPage<TComponent>()
        where TComponent : IComponent
    {
        EnsureConfigurable();
        if (_routeRegistrations.Any(registration => registration.ComponentType == typeof(TComponent)))
        {
            throw new InvalidOperationException("A not-found page cannot also be registered with AddPages.");
        }
        _notFoundComponentType = typeof(TComponent);
        return this;
    }

    /// <summary>Registers parameter sets for a component declaring exactly one parameterized route.</summary>
    /// <param name="parameters">A deferred factory evaluated per snapshot; each object supplies route values and component parameters.</param>
    /// <remarks>No assembly registration is required; multiple registrations concatenate their results, which may be empty.</remarks>
    public StaticSite AddPages<TPage>(Func<IServiceProvider, IEnumerable<object>> parameters)
        where TPage : IComponent
    {
        EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(parameters);

        if (_notFoundComponentType == typeof(TPage))
        {
            throw new InvalidOperationException("A not-found page cannot also be registered with AddPages.");
        }

        _routeRegistrations.Add(new RouteRegistration(
            typeof(TPage),
            () => [.. parameters(ServiceProvider).Select(ConvertRouteValues)]));
        return this;
    }

    /// <summary>Registers a site-wide output file written after all pages are rendered.</summary>
    public StaticSite AddArtifact(
        string outputRelativePath,
        Func<Stream, SiteOutputContext, CancellationToken, Task> write)
    {
        EnsureConfigurable();
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRelativePath);
        ArgumentNullException.ThrowIfNull(write);

        _artifacts.Add(new SiteArtifactRegistration(outputRelativePath, write));
        return this;
    }

    /// <summary>Publishes or serves the site according to the MSBuild execution context.</summary>
    /// <remarks>Await this method in the entry point so <c>dotnet publish</c> generates the site.</remarks>
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
        if (_hasRun)
        {
            throw new InvalidOperationException("A StaticSite can only be run once.");
        }
        _hasRun = true;

        try
        {
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
                    await PublishAsync(outputPath!, cts.Token);
                    return 0;
                }

                await ServeAsync(cts.Token);
                return 0;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Stopping the dev server with Ctrl+C is a normal exit; an interrupted
                // generation left partial output and is reported as failure.
                return publishing ? 1 : 0;
            }
        }
        finally
        {
            await DisposeAsync();
        }
    }

    /// <summary>Generates the site, reusing unchanged output from a previous publish.</summary>
    /// <param name="outputPath">An absolute output directory or a path relative to <see cref="SitePaths.RootDirectory"/>.</param>
    /// <remarks>Repeated publication uses the same output directory; switching directories or execution modes requires a new site.</remarks>
    internal async Task PublishAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        FreezeConfiguration();

        var options = Paths.ResolveForPublish(outputPath);
        OutputPathValidator.Validate(options, Paths.RootDirectory, Paths.ResolveKijiPath());
        UseRunOptions(options);
        InvalidateContent();

        var stopwatch = Stopwatch.StartNew();
        var phases = new BuildPhaseTimer();
        var snapshot = CreateSnapshot();
        var renderer = GetRenderer();

        StaticSiteGenerator.ValidateNoStaticFileCollisions(options, snapshot.Pages);
        phases.Mark(BuildPhaseTimer.Snapshot);

        var planner = new IncrementalBuildPlanner(
            options,
            Paths.RootDirectory,
            Paths.ResolveCachePath(),
            Info,
            _buildInputPaths,
            _buildInputValues,
            _services!.GetService<ContentFileRegistry>());

        // Helpers and custom encoder factories may live outside the page assemblies.
        // Their code is a build input even when pages only reach it through injection.
        var plan = await planner.CreatePlanAsync(
            snapshot.Pages,
            [.. _pageAssemblies,
                .. _routeRegistrations.Select(static registration => registration.ComponentType.Assembly),
                .. _pageServiceTypes.Select(static type => type.Assembly),
                _imageProcessorFactory.Method.Module.Assembly,
                .. _notFoundComponentType is { } notFound ? new[] { notFound.Assembly } : [],
                typeof(StaticSite).Assembly],
            _forceFullBuild,
            cancellationToken);
        phases.Mark(BuildPhaseTimer.Plan);

        // The output directory is kept, never wiped: whatever this build does not
        // produce is deleted by the reconciliation pass below, which is both cheaper
        // than a delete-and-rewrite and checked against the directory itself.
        Directory.CreateDirectory(options.OutputDirectory);
        phases.Mark(BuildPhaseTimer.Clean);

        var staticFiles = await planner.SyncStaticFilesAsync(plan);
        phases.Mark(BuildPhaseTimer.Static);

        // Render only the pages the plan could not prove unchanged, recording what
        // each render reads and writes for the next build's skip checks. The previous
        // manifest comes along so a render that reproduces the bytes already on disk
        // skips the write; with KijiForce there is no manifest and everything is written.
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

    /// <summary>Starts the development server with automatic browser reload on content changes.</summary>
    /// <remarks>The address comes from ASP.NET Core configuration, defaulting to <c>http://127.0.0.1:8080</c>.</remarks>
    internal async Task ServeAsync(CancellationToken cancellationToken = default)
    {
        var (devServer, web) = await StartDevServerAsync(cancellationToken);
        await using (devServer)
        {
            await web.WaitForShutdownAsync(cancellationToken);
        }
    }

    internal async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

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
        return StartDevServerAsync(_args, cancellationToken);
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
        FreezeConfiguration();
        var options = UseRunOptions(Paths.ResolveForServe());
        EnsureServices();

        var devServer = new DevServer(this, reporter);
        try
        {
            var web = await devServer.StartAsync(options, args, cancellationToken);
            return (devServer, web);
        }
        catch
        {
            await devServer.DisposeAsync();
            throw;
        }
    }

    internal Task<string> RenderPageAsync(PageRenderRequest request, CancellationToken cancellationToken)
    {
        return RenderPageAsync(GetRenderer(), request, cancellationToken);
    }

    internal void InvalidateContent()
    {
        _runtime.Invalidate();
    }

    internal SiteSnapshot CreateSnapshot()
    {
        FreezeConfiguration();
        // Planning expands route factories, which read content. If nothing has settled
        // the options yet, fall back to paths that cannot be mistaken for a deliverable.
        UseOptions(Paths.ResolveForPlanning());
        EnsureServices();

        var snapshotPhases = new BuildPhaseTimer();
        var scanned = new List<PageDiscovery.DiscoveredPage>();
        foreach (var assembly in _pageAssemblies)
        {
            scanned.AddRange(PageDiscovery.FromAssembly(assembly));
        }

        foreach (var componentType in _routeRegistrations.Select(static registration => registration.ComponentType).Distinct())
        {
            scanned.Add(FindDynamicPage(PageDiscovery.FromTypes([componentType]), componentType));
        }

        var discovered = PageDiscovery.EnsureUniqueRoutes(ApplyNotFoundOverride(scanned));

        snapshotPhases.Mark(BuildPhaseTimer.Discovery);

        var dynamicRoutes = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
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
            discovered,
            dynamicRoutes);

        var requests = plannedPages
            .Select(request => request with { RootParameters = CreateRootParameters(request) })
            .ToList();

        snapshotPhases.Mark(BuildPhaseTimer.Planning);

        return new SiteSnapshot(requests);
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
        IReadOnlyList<IReadOnlyDictionary<string, string>> entries)
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
            var suppliedNames = entry.Keys.ToHashSet(StringComparer.Ordinal);
            var missing = routeParameterNames
                .Where(name => !suppliedNames.Contains(name))
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Route mapping for '{page.ComponentType.FullName}' did not supply required route values for '{page.SourceIdentifier}': {string.Join(", ", missing.Select(static name => $"'{name}'"))}.");
            }

            foreach (var (name, value) in entry)
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

        var candidates = PageDiscovery.FromTypes([_notFoundComponentType]);
        if (candidates.Count != 1)
        {
            throw new InvalidOperationException(
                $"Not-found component '{_notFoundComponentType.FullName}' must declare exactly one '@page' route template.");
        }
        var overridden = StaticPageDefinition.Create(
            "/404.html",
            routePathOverride: "/404.html",
            outputRelativePathOverride: "404.html");

        return [.. discovered.Where(page => page.ComponentType != _notFoundComponentType),
            new PageDiscovery.DiscoveredPage(overridden.SourceIdentifier, _notFoundComponentType, overridden)];
    }

    private async Task<string> RenderPageAsync(ComponentRenderer renderer, PageRenderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PageRenderContext.SetCurrent(CreatePageRenderContext(request));
        try
        {
            return await renderer.RenderComponentAsync<KijiRoot>(
                request.RootParameters ?? CreateRootParameters(request),
                new Uri(Info.BaseUrl, request.RoutePath.TrimStart('/')));
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
                new Uri(Info.BaseUrl, request.RoutePath.TrimStart('/')));
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
            Info,
            [.. snapshot.Pages.Select(static page => new SitePageInfo(
                page.RoutePath.TrimStart('/'),
                page.OutputRelativePath))],
            ServiceProvider);
    }

    private async Task<IReadOnlyList<string>> GenerateArtifactsAsync(ResolvedSitePaths options, SiteSnapshot snapshot, CancellationToken cancellationToken)
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
            var fullPath = OutputPathValidator.ResolveUnderRoot(
                options.OutputDirectory,
                artifact.OutputRelativePath,
                "Artifact output path");
            if (reservedPaths.TryGetValue(fullPath, out var collisionTarget))
            {
                throw new InvalidOperationException(
                    $"Artifact output path '{artifact.OutputRelativePath}' collides with {collisionTarget}.");
            }

            reservedPaths[fullPath] = "another artifact output path";
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
            await artifact.WriteAsync(stream, context, cancellationToken);

            artifactRelativePaths.Add(Path.GetRelativePath(options.OutputDirectory, fullPath));
            BuildOutput.Info($"Generated: {fullPath}");
        }

        return artifactRelativePaths;
    }

    private static Dictionary<string, string> CreateReservedArtifactPaths(ResolvedSitePaths options, SiteSnapshot snapshot)
    {
        var reservedPaths = snapshot.Pages.ToDictionary(
            page => OutputPathValidator.ResolveUnderRoot(
                options.OutputDirectory,
                page.OutputRelativePath,
                "Page output path"),
            static _ => "a generated page output path",
            StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(options.StaticDirectory))
        {
            return reservedPaths;
        }

        foreach (var file in Directory.EnumerateFiles(options.StaticDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(options.StaticDirectory, file);
            var outputPath = OutputPathValidator.ResolveUnderRoot(
                options.OutputDirectory,
                relativePath,
                "Static file output path");
            reservedPaths.TryAdd(outputPath, "a static file output path");
        }

        return reservedPaths;
    }

    /// <summary>
    /// Settles the paths the running command works against. First caller wins:
    /// <see cref="ResolvedSitePaths"/> is a singleton, so what is fixed here is what every
    /// loader and renderer sees for the rest of the process.
    /// </summary>
    private ResolvedSitePaths UseOptions(ResolvedSitePaths options)
    {
        _activeOptions ??= options;
        return _activeOptions;
    }

    private static IReadOnlyDictionary<string, string> ConvertRouteValues(object values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var entries = values is IReadOnlyDictionary<string, string> strings
            ? strings.Select(static pair => new KeyValuePair<string, object?>(pair.Key, pair.Value))
            : new RouteValueDictionary(values);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in entries)
        {
            var converted = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(converted))
            {
                throw new InvalidOperationException(
                    $"Route value '{name}' on '{values.GetType().Name}' resolved to null or whitespace.");
            }

            result.Add(name, converted);
        }

        return result;
    }

    private ResolvedSitePaths UseRunOptions(ResolvedSitePaths options)
    {
        if (_activeOptions is not null && _activeOptions != options)
        {
            throw new InvalidOperationException(
                "This StaticSite has already settled its paths. Create a new app to use a different output directory or switch between publishing and serving.");
        }

        return UseOptions(options);
    }

    /// <summary>
    /// Settles on planning paths without generating anything. For tests that load content
    /// directly; a real site reaches this through <see cref="RunAsync(CancellationToken)"/>.
    /// </summary>
    internal void UsePlanningOptions()
    {
        FreezeConfiguration();
        UseOptions(Paths.ResolveForPlanning());
    }

    private void EnsureServices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FreezeConfiguration();
        if (_services is not null)
        {
            return;
        }

        var services = new ServiceCollection();
        ComponentRenderer.AddComponentRenderingServices(services);
        services.AddSingleton(Info);
        // Deliberately not defaulted: the run decides these paths (a publish writes to
        // the directory dotnet publish chose, the dev server to its own mirror) and, as a
        // singleton, the first resolution wins for the whole process. No public API hands
        // out a provider before the run starts, so this is an invariant rather than a
        // user-facing error — but a default here would silently pin publish paths onto
        // the dev server.
        services.AddSingleton(_ => _activeOptions ?? throw new InvalidOperationException(
            "ResolvedSitePaths was resolved before a command settled the site's paths."));
        _runtime.ApplyRegistrations(services);

        services.AddSingleton<IImageProcessor>(_ => _imageProcessorFactory()
            ?? throw new InvalidOperationException("The image processor factory returned null."));
        services.AddSingleton<ContentFileRegistry>();

        foreach (var type in _pageServiceTypes)
        {
            if (services.Any(descriptor => descriptor.ServiceType == type
                || (type.IsGenericType && descriptor.ServiceType == type.GetGenericTypeDefinition())))
            {
                throw new InvalidOperationException($"Page service '{type.FullName}' conflicts with a service managed by Kiji.");
            }
        }

        foreach (var type in _pageServiceTypes)
        {
            services.AddScoped(type);
        }

        _services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        _runtime.Attach(_services);
    }

    internal void EnsureConfigurable()
    {
        if (_configurationFrozen)
        {
            throw new InvalidOperationException("Site configuration cannot be changed after execution has started. Create a new StaticSite to configure another site.");
        }
    }

    private void FreezeConfiguration()
    {
        if (_configurationFrozen)
        {
            return;
        }

        _ = Info;
        Paths.Freeze();
        _configurationFrozen = true;
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
        _renderer = ComponentRenderer.Attach(_services!, Info.BaseUrl);
        return _renderer;
    }

    private sealed record RouteRegistration(
        Type ComponentType,
        Func<IReadOnlyList<IReadOnlyDictionary<string, string>>> CreateEntries);
}
