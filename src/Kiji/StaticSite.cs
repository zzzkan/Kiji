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
    private readonly List<string> _contentPaths = [];
    private readonly List<Action<IServiceCollection>> _configureServices = [];
    private IReadOnlyCollection<Assembly> _serviceAssemblies = [];
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

    private StaticSite(string[] args, SiteExecutionPaths paths, Assembly entryAssembly)
    {
        _args = [.. args];
        _pageAssemblies.Add(entryAssembly);
        _runtime.Dependencies.Register("image-processor", () => ImageProcessorIdentity.Get(ServiceProvider.GetRequiredService<IImageProcessor>()));
        Paths = paths;
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

    internal SiteExecutionPaths Paths { get; }

    /// <summary>Configures the services available to components and site factories.</summary>
    /// <remarks>
    /// Callbacks run once in registration order before rendering. Each page render owns a scope.
    /// Content loaders, route/feed factories and artifacts cannot resolve scoped services.
    /// Kiji's managed services cannot be changed. Use AlwaysRender on components reading untracked external state.
    /// </remarks>
    public StaticSite UseServices(Action<IServiceCollection> configure)
    {
        EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(configure);
        _configureServices.Add(configure);
        return this;
    }

    internal string[] ContentRoots { get; private set; } = [];

    // Content must not resolve execution paths before publish/serve has settled them.
    internal IServiceProvider ServiceProvider
    {
        get
        {
            EnsureServices();
            return _services!;
        }
    }

    /// <summary>
    /// Creates a site that automatically discovers routed components in the entry assembly.
    /// </summary>
    /// <param name="args">Arguments forwarded to ASP.NET Core configuration when serving.</param>
    public static StaticSite Create(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var entryAssembly = Assembly.GetEntryAssembly() ?? throw new InvalidOperationException(
            "No entry assembly is available in this host. Run the site project as an application with dotnet run, dotnet watch or dotnet publish.");

        return new StaticSite(args, SiteExecutionPaths.FromRuntimeMetadata(AppContext.GetData("Kiji.ProjectDirectory"), entryAssembly), entryAssembly);
    }

    internal static StaticSite Create(string[] args, SiteExecutionPaths paths, Assembly entryAssembly) => new(args, paths, entryAssembly);

    /// <summary>Uses a content source resolved as <see cref="ContentDictionary{T}"/>.</summary>
    /// <param name="loader">Loads items lazily once per snapshot, after execution paths are settled.</param>
    public StaticSite UseContentSource<T>(
        Func<IServiceProvider, IReadOnlyList<T>> loader)
        where T : class
    {
        EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(loader);

        return RegisterContent(
            services =>
            {
                var items = loader(services);
                var keyed = new (string Key, T Item, string? Digest)[items.Count];
                for (var i = 0; i < items.Count; i++)
                {
                    keyed[i] = (i.ToString(System.Globalization.CultureInfo.InvariantCulture), items[i], null);
                }
                return keyed;
            });
    }

    internal StaticSite RegisterContent<T>(
        Func<IServiceProvider, IReadOnlyList<(string Key, T Item, string? Digest)>> loader,
        string contentSetScope = "",
        string? directory = null)
        where T : class
    {
        EnsureConfigurable();
        // Source identities use the project directory, including when a caller supplied
        // an absolute path inside the checkout, keeping those identities portable.
        _runtime.Register(() => new ContentDictionary<T>(_runtime, loader, directory is null
            ? contentSetScope
            : Path.GetRelativePath(Path.GetFullPath(Paths.ProjectDirectory), Paths.ResolveAgainstRoot(directory)).Replace('\\', '/')));
        if (directory is not null) { _contentPaths.Add(directory); }
        return this;
    }

    /// <summary>Registers a content source with stable identities and explicit data digests.</summary>
    public StaticSite UseContentSource<T>(string sourceId, Func<IServiceProvider, IReadOnlyList<ContentEntry<T>>> loader)
        where T : class
    {
        EnsureConfigurable();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(loader);
        return RegisterContent<T>(services => [.. loader(services)
            .Select(static entry => (entry.Id, entry.Value, entry.Digest))], sourceId);
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

    /// <summary>Adds an assembly whose public, non-abstract routed components are discovered as pages.</summary>
    /// <remarks>The entry assembly is included automatically. Repeated registration has no effect.</remarks>
    public StaticSite AddPageAssembly(Assembly assembly)
    {
        EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(assembly);

        if (!_pageAssemblies.Contains(assembly)) { _pageAssemblies.Add(assembly); }
        return this;
    }

    /// <summary>Registers a page as <c>404.html</c>.</summary>
    /// <remarks>The component must belong to a page assembly, declare exactly one route, and cannot also receive <c>AddPageParameters&lt;TPage&gt;</c>.</remarks>
    public StaticSite AddNotFoundPage<TPage>()
        where TPage : IComponent
    {
        EnsureConfigurable();
        if (_routeRegistrations.Any(registration => registration.ComponentType == typeof(TPage)))
        {
            throw new InvalidOperationException("A not-found page cannot also receive AddPageParameters<TPage>.");
        }
        _notFoundComponentType = typeof(TPage);
        return this;
    }

    /// <summary>Adds parameter sets rendered at every parameterized route of a page, or at every route when all are fixed.</summary>
    /// <param name="parameters">A factory run once per build; each object is a complete set of route values and component parameters.</param>
    /// <remarks>
    /// The page must belong to the entry assembly or an assembly added with AddPageAssembly. Fixed routes beside
    /// parameterized routes render once with default values. Registrations concatenate; an empty result generates no pages.
    /// Values keep their types and are treated as read-only. Values without a stable fingerprint disable HTML reuse.
    /// </remarks>
    public StaticSite AddPageParameters<TPage>(Func<IServiceProvider, IEnumerable<object>> parameters)
        where TPage : IComponent
    {
        EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(parameters);

        if (_notFoundComponentType == typeof(TPage))
        {
            throw new InvalidOperationException("A not-found page cannot also receive AddPageParameters<TPage>.");
        }

        _routeRegistrations.Add(new RouteRegistration(
            typeof(TPage),
            () => [.. parameters(ServiceProvider).Select(ConvertParameters)]));
        return this;
    }

    /// <summary>Registers a site-wide output file written after all pages are rendered.</summary>
    public StaticSite AddArtifact(
        string outputRelativePath,
        Func<Stream, SiteOutputContext, CancellationToken, Task> write)
        => AddArtifact(outputRelativePath, write, preserveUnchangedOutput: false);

    internal StaticSite AddArtifact(
        string outputRelativePath,
        Func<Stream, SiteOutputContext, CancellationToken, Task> write,
        bool preserveUnchangedOutput)
    {
        EnsureConfigurable();
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRelativePath);
        ArgumentNullException.ThrowIfNull(write);

        _artifacts.Add(new SiteArtifactRegistration(outputRelativePath, write, preserveUnchangedOutput));
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
    /// <param name="outputPath">An absolute output directory or a path relative to the site project directory.</param>
    /// <remarks>Repeated publication uses the same output directory; switching directories or execution modes requires a new site.</remarks>
    internal async Task PublishAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        FreezeConfiguration();

        await using var cacheLease = await CacheLease.AcquireAsync(Paths.ResolveCachePath(), cancellationToken);
        var options = Paths.ResolveForPublish(outputPath);
        var assetManifest = options.AssetManifestPath is null ? new StaticAssetManifest { Assets = [] }
            : StaticAssetManifest.Load(options.AssetManifestPath, options.AssetEndpointsPath, Info.BaseUrl);
        UseAssetResources(assetManifest.Resources);
        var assets = assetManifest.Assets;
        OutputPathValidator.Validate(options, assetManifest, Paths.ProjectDirectory, Paths.ResolveKijiPath(), ContentRoots);
        UseRunOptions(options);
        InvalidateContent();

        var stopwatch = Stopwatch.StartNew();
        var phases = new BuildPhaseTimer();
        var snapshot = CreateSnapshot();
        var renderer = GetRenderer();

        var reservedPaths = CreateReservedArtifactPaths(options, snapshot, assets);
        phases.Mark(BuildPhaseTimer.Snapshot);

        var planner = new IncrementalBuildPlanner(
            options,
            Paths.ProjectDirectory,
            Paths.ResolveCachePath(),
            Info,
            _services!.GetService<ContentFileRegistry>(), _runtime.Dependencies,
            _services!.GetRequiredService<IImageProcessor>(), assetManifest.Resources);

        // Snapshot planning already required every page type to come from a page assembly.
        // Helpers and custom encoder factories may live outside the page assemblies.
        // Their code is a build input even when pages only reach it through injection.
        var plan = await planner.CreatePlanAsync(
            snapshot.Pages,
            [.. _pageAssemblies, .. _serviceAssemblies, typeof(StaticSite).Assembly],
            _forceFullBuild,
            cancellationToken);
        phases.Mark(BuildPhaseTimer.Plan);

        // The output directory is kept, never wiped: whatever this build does not
        // produce is deleted by the reconciliation pass below, which is both cheaper
        // than a delete-and-rewrite and checked against the directory itself.
        Directory.CreateDirectory(options.OutputDirectory);
        phases.Mark(BuildPhaseTimer.Clean);

        var staticFiles = await planner.SyncStaticFilesAsync(assets);
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
            cancellationToken,
            planner.OutputStampsValid);
        phases.Mark(BuildPhaseTimer.Render);

        var artifacts = await GenerateArtifactsAsync(options, snapshot, reservedPaths, cancellationToken);
        phases.Mark(BuildPhaseTimer.Artifacts);

        // Input verification is independent of output reconciliation and entry
        // construction. Join it before publishing the cache, including on failure.
        var verification = recorders.IsEmpty ? Task.CompletedTask : Task.Run(
            () => IncrementalBuildPlanner.VerifyInputs(recorders.Values), cancellationToken);
        BuildManifest manifest;
        try
        {
            // One entry per rendered page, verifying its dependencies: independent
            // work, so it runs in parallel and lands at its own index to keep the manifest
            // order deterministic.
            var renderedEntries = new BuildManifestPage[rendered.Count];
            Parallel.For(0, rendered.Count, index =>
            {
                var page = rendered[index];
                renderedEntries[index] = planner.CreatePageEntry(
                    page.Request,
                    page.OutputHash,
                    recorders[page.Request.OutputRelativePath],
                    page.Html);
            });

            var pageEntries = new List<BuildManifestPage>(plan.CarriedPages.Count + rendered.Count);
            pageEntries.AddRange(plan.CarriedPages);
            pageEntries.AddRange(renderedEntries);
            pageEntries.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(
                left.OutputRelativePath, right.OutputRelativePath));

            phases.Mark(BuildPhaseTimer.Entries);

            manifest = new BuildManifest
            {
                SchemaVersion = BuildManifest.CurrentSchemaVersion,
                OptionsHash = plan.OptionsHash,
                CodeDependencies = plan.CodeDependencies,
                Pages = pageEntries,
                StaticFiles = staticFiles,
                Artifacts = artifacts,
            };

            planner.ReconcileOutputs(manifest);
        }
        finally { await verification; }
        phases.Mark(BuildPhaseTimer.Reconcile);

        planner.SaveManifest(manifest, plan.OldManifest, cancellationToken);
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
        var (devServer, web) = await StartDevServerAsync(_args, cancellationToken);
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

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
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
        var options = UseRunOptions(Paths.ResolveForDevelopment());
        OutputPathValidator.ValidateInputs(options.OutputDirectory, ContentRoots);
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

    internal async Task<string> RenderPageAsync(PageRenderRequest request, CancellationToken cancellationToken,
        SharedRenderLifetime? sharedRenders = null)
    {
        using var output = new StringWriter();
        await RenderPageAsync(GetRenderer(), request, output, dependencies: null, cancellationToken, sharedRenders);
        return output.ToString();
    }

    internal void InvalidateContent()
    {
        _runtime.Invalidate();
        _services?.GetService<ContentFileRegistry>()?.Invalidate();
    }

    internal SiteSnapshot CreateSnapshot()
    {
        FreezeConfiguration();
        // Planning expands route factories, which read content. If nothing has settled
        // the options yet, fall back to paths that cannot be mistaken for a deliverable.
        _activeOptions ??= Paths.ResolveForDevelopment();
        EnsureServices();

        var snapshotPhases = new BuildPhaseTimer();
        var scanned = new List<PageDiscovery.DiscoveredPage>();
        foreach (var assembly in _pageAssemblies)
        {
            scanned.AddRange(PageDiscovery.FromAssembly(assembly));
        }

        var parameterRoutes = _routeRegistrations.Select(static registration => registration.ComponentType)
            .Distinct().ToDictionary(static type => type, FindParameterRoutes);
        // Registered non-public components are pages too, with all of their routes.
        var scannedTypes = scanned.Select(static page => page.ComponentType).ToHashSet();
        scanned.AddRange(parameterRoutes.Keys.Where(type => !scannedTypes.Contains(type)).SelectMany(PageDiscovery.FromType));

        var discovered = PageDiscovery.EnsureUniqueRoutes(ApplyNotFoundOverride(scanned));

        snapshotPhases.Mark(BuildPhaseTimer.Discovery);

        var pageParameters = new Dictionary<string, List<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in _routeRegistrations)
        {
            var routes = parameterRoutes[registration.ComponentType];
            var entries = registration.CreateEntries();
            snapshotPhases.Mark(BuildPhaseTimer.RouteEntries);
            ValidateRouteEntries(registration.ComponentType, routes, entries);
            snapshotPhases.Mark(BuildPhaseTimer.RouteValidation);

            foreach (var route in routes)
            {
                if (!pageParameters.TryGetValue(route.SourceIdentifier, out var combined))
                {
                    pageParameters.Add(route.SourceIdentifier, combined = []);
                }
                combined.AddRange(entries);
            }
        }

        // Expanding a route factory is where content is first read, so this is where
        // loading and parsing every markdown file lands.
        snapshotPhases.Mark(BuildPhaseTimer.Routes);

        var plannedPages = StaticPagePlanner.PlanPages(
            discovered,
            pageParameters);

        var requests = plannedPages
            .Select(request => request with { RootParameters = CreateRootParameters(request) })
            .ToList();

        snapshotPhases.Mark(BuildPhaseTimer.Planning);

        return new SiteSnapshot(requests);
    }

    /// <summary>
    /// Selects the routes receiving AddPageParameters: all parameterized routes of the component,
    /// or all its routes when every route is fixed. Fixed routes beside parameterized ones keep their default rendering.
    /// </summary>
    private IReadOnlyList<PageDiscovery.DiscoveredPage> FindParameterRoutes(Type componentType)
    {
        ValidatePageAssembly(componentType);
        var pages = PageDiscovery.FromType(componentType);
        var parameterized = pages.Where(static page => page.PageDefinition.IsDynamic).ToArray();
        return parameterized.Length > 0 ? parameterized : pages;
    }

    private void ValidatePageAssembly(Type componentType)
    {
        if (!_pageAssemblies.Contains(componentType.Assembly))
        {
            throw new InvalidOperationException(
                $"Page component '{componentType.FullName}' belongs to an unregistered assembly. Call AddPageAssembly(typeof({componentType.Name}).Assembly) before execution.");
        }
    }

    private static void ValidateRouteEntries(
        Type componentType,
        IReadOnlyList<PageDiscovery.DiscoveredPage> routes,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> entries)
    {
        var declaredParameters = PageDiscovery.Parameters(componentType);
        var routeList = string.Join(", ", routes.Select(static route => $"'{route.SourceIdentifier}'"));

        foreach (var entry in entries)
        {
            foreach (var route in routes)
            {
                var missing = route.PageDefinition.ParameterNames
                    .Where(name => !entry.ContainsKey(name))
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray();
                if (missing.Length > 0)
                {
                    throw new InvalidOperationException(
                        $"Route mapping for '{componentType.FullName}' did not supply required route values for '{route.SourceIdentifier}': {string.Join(", ", missing.Select(static name => $"'{name}'"))}.");
                }
            }

            foreach (var (name, value) in entry)
            {
                if (!declaredParameters.TryGetValue(name, out var property))
                {
                    throw new InvalidOperationException(
                        $"Route mapping for '{componentType.FullName}' ({routeList}) supplied '{name}', which is not a declared '[Parameter]' property.");
                }
                if (!property.IsWritable)
                {
                    throw new InvalidOperationException(
                        $"Route mapping for '{componentType.FullName}' ({routeList}) supplied parameter '{name}', which must have a public setter and cannot be an indexer.");
                }
                var type = property.PropertyType;
                var compatible = value is null
                    ? !type.IsValueType || Nullable.GetUnderlyingType(type) is not null
                    : type.IsInstanceOfType(value);
                if (!compatible)
                {
                    throw new InvalidOperationException(
                        $"Route mapping for '{componentType.FullName}' ({routeList}) supplied parameter '{name}': expected '{type.FullName}', actual '{value?.GetType().FullName ?? "null"}'. Implicit parameter conversions are not supported.");
                }
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

        ValidatePageAssembly(_notFoundComponentType);
        var candidates = PageDiscovery.FromType(_notFoundComponentType);
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

    private async Task RenderPageAsync(
        ComponentRenderer renderer,
        PageRenderRequest request,
        TextWriter output,
        BuildDependencyRecorder? dependencies,
        CancellationToken cancellationToken,
        SharedRenderLifetime? sharedRenders = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PageRenderContext.SetCurrent(CreatePageRenderContext(request, dependencies, sharedRenders, cancellationToken));
        try
        {
            await renderer.RenderComponentToAsync<KijiRoot>(
                output,
                request.RootParameters ?? CreateRootParameters(request));
            // Component lifecycle methods can absorb cancellation exceptions.
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            PageRenderContext.SetCurrent(null);
        }
    }

    private PageRenderContext CreatePageRenderContext(PageRenderRequest request, BuildDependencyRecorder? dependencies,
        SharedRenderLifetime? sharedRenders, CancellationToken cancellationToken)
    {
        var outputRelativeDirectory = Path.GetDirectoryName(request.OutputRelativePath) ?? string.Empty;

        return new PageRenderContext
        {
            RoutePath = request.RoutePath,
            OutputRelativeDirectory = outputRelativeDirectory,
            OutputUrlDirectory = PageRenderContext.CreateOutputUrlDirectory(Info.BaseUrl, outputRelativeDirectory),
            Dependencies = dependencies,
            CancellationToken = cancellationToken,
            SharedRenders = sharedRenders,
            Assets = _assetResources,
        };
    }

    private AssetResources _assetResources = AssetResources.Empty;

    internal void UseAssetResources(AssetResources resources) => _assetResources = resources;

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

    private async Task<IReadOnlyList<string>> GenerateArtifactsAsync(ResolvedSitePaths options, SiteSnapshot snapshot,
        Dictionary<string, string> reservedPaths, CancellationToken cancellationToken)
    {
        if (_artifacts.Count == 0)
        {
            return [];
        }

        var artifactRelativePaths = new List<string>(_artifacts.Count);
        var context = CreateOutputContext(snapshot);

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

            await artifact.WriteFileAsync(fullPath, context, cancellationToken);

            artifactRelativePaths.Add(Path.GetRelativePath(options.OutputDirectory, fullPath));
            BuildOutput.Info($"Generated: {fullPath}");
        }

        return artifactRelativePaths;
    }

    private static Dictionary<string, string> CreateReservedArtifactPaths(ResolvedSitePaths options, SiteSnapshot snapshot, IReadOnlyList<StaticAsset> assets)
    {
        var reservedPaths = snapshot.Pages.ToDictionary(
            page => OutputPathValidator.ResolveUnderRoot(
                options.OutputDirectory,
                page.OutputRelativePath,
                "Page output path"),
            static _ => "a generated page output path",
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in assets)
        {
            var relativePath = file.Target;
            var outputPath = OutputPathValidator.ResolveUnderRoot(
                options.OutputDirectory,
                relativePath,
                "Static file output path");
            if (reservedPaths.TryGetValue(outputPath, out var existing) && existing == "a generated page output path")
            {
                throw new InvalidOperationException(
                    $"Static file '{relativePath}' collides with a generated page output path. Rename the static file or change the page route.");
            }
            reservedPaths[outputPath] = "a static file output path";
        }

        return reservedPaths;
    }

    private static IReadOnlyDictionary<string, object?> ConvertParameters(object values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new RouteValueDictionary(values)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private ResolvedSitePaths UseRunOptions(ResolvedSitePaths options)
    {
        if (_activeOptions is not null && _activeOptions != options)
        {
            throw new InvalidOperationException(
                "This StaticSite has already settled its paths. Create a new app to use a different output directory or switch between publishing and serving.");
        }

        return _activeOptions = options;
    }

    /// <summary>
    /// Settles on planning paths without generating anything. For tests that load content
    /// directly; a real site reaches this through <see cref="RunAsync(CancellationToken)"/>.
    /// </summary>
    internal void UsePlanningOptions()
    {
        FreezeConfiguration();
        _activeOptions ??= Paths.ResolveForDevelopment();
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
        // A default here would pin the singleton to the wrong execution mode.
        services.AddSingleton(_ => _activeOptions ?? throw new InvalidOperationException(
            "ResolvedSitePaths was resolved before a command settled the site's paths."));
        _runtime.ApplyRegistrations(services);

        services.AddSingleton<IImageProcessor>(static _ => new ImageProcessor());
        services.AddSingleton<ContentFileRegistry>();

        var managed = services.Where(static descriptor => SiteServiceRegistration.IsManaged(descriptor.ServiceType)).ToArray();
        foreach (var configure in _configureServices)
        {
            configure(services);
        }
        services.MakeReadOnly();

        SiteServiceRegistration.Validate(services, managed);
        _serviceAssemblies = SiteServiceRegistration.CollectAssemblies(services, _configureServices);
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
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        ContentRoots = [.. _contentPaths.Select(Paths.ResolveAgainstRoot).Distinct(comparer)];
        _configurationFrozen = true;
    }

    private ComponentRenderer GetRenderer()
    {
        if (_renderer is not null)
        {
            return _renderer;
        }

        EnsureServices();

        _renderer = new ComponentRenderer(_services!);
        return _renderer;
    }

    private sealed record RouteRegistration(
        Type ComponentType,
        Func<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CreateEntries);
}
