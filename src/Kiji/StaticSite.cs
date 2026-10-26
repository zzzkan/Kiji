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
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kiji;

/// <summary>Generates and serves a static website configured with <see cref="StaticSiteBuilder"/>.</summary>
public sealed class StaticSite : IAsyncDisposable
{
    private readonly string[] _args;
    private readonly SiteRunSettings _settings;
    private readonly ContentRuntime _runtime;
    private readonly SiteHostEnvironment _environment;
    private readonly SiteConfiguration _configuration;
    private readonly IReadOnlyList<PageParameterRegistration> _pageParameters;
    private readonly IReadOnlyList<SiteArtifactRegistration> _artifacts;
    private readonly IReadOnlyList<Assembly> _pageAssemblies;
    private readonly IReadOnlyCollection<Assembly> _serviceAssemblies;
    private readonly Type? _defaultLayoutType;
    private readonly Type? _notFoundComponentType;
    private readonly ServiceProvider _services;
    private ComponentRenderer? _renderer;
    private ResolvedSitePaths? _activeOptions;
    private bool _forceFullBuild;
    private bool _hasRun;
    private bool _disposed;

    internal StaticSite(StaticSiteBuilder builder)
    {
        _args = builder.Args;
        _settings = builder.RunSettings;
        _runtime = builder.Runtime;
        _environment = builder.HostEnvironment;
        _configuration = builder.HostConfiguration;
        _pageParameters = [.. builder.PageParameters];
        _artifacts = [.. builder.Artifacts];
        _pageAssemblies = [.. builder.PageAssemblies];
        _defaultLayoutType = builder.DefaultLayoutType;
        _notFoundComponentType = builder.NotFoundComponentType;
        Paths = builder.Paths;
        SiteInfo = builder.SiteInfo;

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        ContentRoots = [.. builder.ContentPaths.Select(Paths.ResolveAgainstRoot).Distinct(comparer)];

        _runtime.Dependencies.Register("image-processor", () => ImageProcessorIdentity.Get(ServiceProvider.GetRequiredService<IImageProcessor>()));
        (_services, _serviceAssemblies) = builder.BuildServices(() => _activeOptions ?? throw new InvalidOperationException(
            "ResolvedSitePaths was resolved before a command settled the site's paths."));
        _runtime.Attach(_services);
    }

    internal SiteInfo SiteInfo { get; }

    internal SiteExecutionPaths Paths { get; }

    internal string[] ContentRoots { get; }

    // Created on first use, so building the site creates no services.
    private ILogger BuildLogger => field ??= ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(BuildLoggerExtensions.CategoryName);

    // Content must not resolve execution paths before publish/serve has settled them.
    internal IServiceProvider ServiceProvider
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _services;
        }
    }

    /// <summary>Creates a builder for a site that discovers routed components in the entry assembly.</summary>
    /// <param name="args">Arguments forwarded to ASP.NET Core configuration when serving.</param>
    public static StaticSiteBuilder CreateBuilder(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var settings = SiteRunSettings.FromEnvironment(Environment.GetEnvironmentVariable);
        if (settings.Publishing)
        {
            // Before anything can fail, including in Program.cs after this call.
            UnhandledPublishErrorReporter.Register();
        }
        var entryAssembly = Assembly.GetEntryAssembly() ?? throw new InvalidOperationException(
            "No entry assembly is available in this host. Run the site project as an application with dotnet run, dotnet watch or dotnet publish.");

        return new StaticSiteBuilder(
            args,
            SiteExecutionPaths.FromRuntimeMetadata(AppContext.GetData("Kiji.ProjectDirectory"), entryAssembly),
            entryAssembly,
            settings);
    }

    /// <param name="args">Arguments forwarded to ASP.NET Core configuration when serving.</param>
    /// <param name="paths">The project and asset paths.</param>
    /// <param name="entryAssembly">The assembly whose routed components are discovered.</param>
    /// <param name="environment">
    /// Reads an environment variable; by default none is set. Injected so tests can exercise
    /// both modes without mutating the process environment, which parallel test classes share.
    /// </param>
    internal static StaticSiteBuilder CreateBuilder(string[] args, SiteExecutionPaths paths, Assembly entryAssembly,
        Func<string, string?>? environment = null)
        => new(args, paths, entryAssembly, SiteRunSettings.FromEnvironment(environment ?? (static _ => null)));

    /// <summary>Publishes or serves the site according to the MSBuild execution context, then disposes the site.</summary>
    /// <remarks>Await this method in the entry point so <c>dotnet publish</c> generates the site.</remarks>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (_hasRun)
        {
            throw new InvalidOperationException("A StaticSite can only be run once.");
        }
        _hasRun = true;

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            void HandleShutdownSignal(PosixSignalContext context)
            {
                context.Cancel = true;
                cts.Cancel();
            }

            using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, HandleShutdownSignal);
            using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, HandleShutdownSignal);

            try
            {
                if (_settings.OutputPath is { } outputPath)
                {
                    _forceFullBuild = _settings.Force;
                    await PublishAsync(outputPath, cts.Token);
                    return 0;
                }

                await ServeAsync(cts.Token);
                return 0;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Stopping the dev server with Ctrl+C is a normal exit; an interrupted
                // generation left partial output and is reported as failure.
                return _settings.Publishing ? 1 : 0;
            }
        }
        catch (Exception exception) when (LogPublishFailure(exception))
        {
            // Not reached: the filter only logs, before the finally below disposes the logging services.
            throw;
        }
        finally
        {
            await DisposeAsync();
        }
    }

    /// <summary>
    /// Logs a failed publish, as the generic host logs a failed start, so the site's logging
    /// settings apply and MSBuild reports it. While serving, the failure reaches the terminal as is.
    /// </summary>
    /// <returns>Always <see langword="false"/>, so the exception keeps propagating. A filter that
    /// throws also counts as false, so a logging failure cannot hide the original exception.</returns>
    private bool LogPublishFailure(Exception exception)
    {
        if (_settings.Publishing && !_disposed)
        {
            BuildLogger.PublishFailed(exception);
            UnhandledPublishErrorReporter.MarkLogged(exception);
        }
        return false;
    }

    /// <summary>Releases the site's services. <see cref="RunAsync"/> disposes the site when it finishes.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        await _services.DisposeAsync();
        _environment.DisposeContentRootFileProvider();
        _configuration.Dispose();
    }

    /// <summary>Generates the site, reusing unchanged output from a previous publish.</summary>
    /// <param name="outputPath">An absolute output directory or a path relative to the site project directory.</param>
    /// <remarks>Repeated publication uses the same output directory; switching directories or execution modes requires a new site.</remarks>
    internal async Task PublishAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await using var cacheLease = await CacheLease.AcquireAsync(Paths.ResolveCachePath(), cancellationToken);
        var options = Paths.ResolveForPublish(outputPath);
        var assetManifest = options.AssetManifestPath is null ? new StaticAssetManifest { Assets = [] }
            : StaticAssetManifest.Load(options.AssetManifestPath, options.AssetEndpointsPath, SiteInfo.BaseUrl);
        UseAssetResources(assetManifest.Resources);
        var assets = assetManifest.Assets;
        OutputPathValidator.Validate(options, assetManifest, Paths.ProjectDirectory, Paths.ResolveKijiPath(), ContentRoots);
        UseRunOptions(options);
        InvalidateContent();
        // After the paths settle, so validators can read content, and before any output.
        ValidateOnStart();

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
            SiteInfo,
            _environment.EnvironmentName,
            _configuration,
            _runtime.Files, _runtime.Dependencies,
            _services.GetRequiredService<IImageProcessor>(), assetManifest.Resources, BuildLogger);

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
            planner.OutputStampsValid,
            BuildLogger);
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
        if (reproduced > 0) { BuildLogger.PagesGeneratedWithReproduced(written, plan.CarriedPages.Count, reproduced, stopwatch.ElapsedMilliseconds); }
        else { BuildLogger.PagesGenerated(written, plan.CarriedPages.Count, stopwatch.ElapsedMilliseconds); }
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        var options = UseRunOptions(Paths.ResolveForDevelopment());
        OutputPathValidator.ValidateInputs(options.OutputDirectory, ContentRoots);
        ValidateOnStart();

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

    internal void InvalidateContent() => _runtime.Invalidate();

    internal SiteSnapshot CreateSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Planning expands page parameter factories, which read content. If nothing has settled
        // the options yet, fall back to paths that cannot be mistaken for a deliverable.
        UsePlanningOptions();

        var snapshotPhases = new BuildPhaseTimer();
        var scanned = new List<PageDiscovery.DiscoveredPage>();
        foreach (var assembly in _pageAssemblies)
        {
            scanned.AddRange(PageDiscovery.FromAssembly(assembly));
        }

        var parameterRoutes = _pageParameters.Select(static registration => registration.ComponentType)
            .Distinct().ToDictionary(static type => type, FindParameterRoutes);
        // Registered non-public components are pages too, with all of their routes.
        var scannedTypes = scanned.Select(static page => page.ComponentType).ToHashSet();
        scanned.AddRange(parameterRoutes.Keys.Where(type => !scannedTypes.Contains(type)).SelectMany(PageDiscovery.FromType));

        var discovered = PageDiscovery.EnsureUniqueRoutes(ApplyNotFoundOverride(scanned));

        snapshotPhases.Mark(BuildPhaseTimer.Discovery);

        var pageParameters = new Dictionary<string, List<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in _pageParameters)
        {
            var routes = parameterRoutes[registration.ComponentType];
            IReadOnlyList<IReadOnlyDictionary<string, object?>> entries =
                [.. registration.Parameters(ServiceProvider).Select(ConvertParameters)];
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

        // Expanding a page parameter factory is where content is first read, so this is where
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
                $"Page component '{componentType.FullName}' belongs to an unregistered assembly. Call AddPageAssembly(typeof({componentType.Name}).Assembly) before building the site.");
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
                        $"Page parameters for '{componentType.FullName}' did not supply required route values for '{route.SourceIdentifier}': {string.Join(", ", missing.Select(static name => $"'{name}'"))}.");
                }
            }

            foreach (var (name, value) in entry)
            {
                if (!declaredParameters.TryGetValue(name, out var property))
                {
                    throw new InvalidOperationException(
                        $"Page parameters for '{componentType.FullName}' ({routeList}) supplied '{name}', which is not a declared '[Parameter]' property.");
                }
                if (!property.IsWritable)
                {
                    throw new InvalidOperationException(
                        $"Page parameters for '{componentType.FullName}' ({routeList}) supplied parameter '{name}', which must have a public setter and cannot be an indexer.");
                }
                var type = property.PropertyType;
                var compatible = value is null
                    ? !type.IsValueType || Nullable.GetUnderlyingType(type) is not null
                    : type.IsInstanceOfType(value);
                if (!compatible)
                {
                    throw new InvalidOperationException(
                        $"Page parameters for '{componentType.FullName}' ({routeList}) supplied parameter '{name}': expected '{type.FullName}', actual '{value?.GetType().FullName ?? "null"}'. Implicit parameter conversions are not supported.");
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
            OutputUrlDirectory = PageRenderContext.CreateOutputUrlDirectory(SiteInfo.BaseUrl, outputRelativeDirectory),
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
            SiteInfo,
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
            BuildLogger.ArtifactGenerated(fullPath);
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
                "This StaticSite has already settled its paths. Build a new site to use a different output directory or switch between publishing and serving.");
        }

        _configuration.Freeze();
        return _activeOptions = options;
    }

    /// <summary>
    /// Settles on planning paths without generating anything. For tests that load content
    /// directly; a real site reaches this through <see cref="RunAsync(CancellationToken)"/>.
    /// </summary>
    internal void UsePlanningOptions()
    {
        _configuration.Freeze();
        _activeOptions ??= Paths.ResolveForDevelopment();
    }

    private ComponentRenderer GetRenderer()
    {
        return _renderer ??= new ComponentRenderer(ServiceProvider);
    }

    /// <summary>
    /// Runs the options validation that <c>ValidateOnStart</c> registers, as the generic host does when it
    /// starts: after Build, before any output. A failure surfaces in RunAsync, which then disposes the
    /// services asynchronously.
    /// </summary>
    private void ValidateOnStart()
    {
        // .NET 10 has only the synchronous validator; use IAsyncStartupValidator from .NET 11.
        ServiceProvider.GetService<IStartupValidator>()?.Validate();
    }
}
