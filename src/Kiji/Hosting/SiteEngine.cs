using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Kiji.Assets;
using Kiji.Components;
using Kiji.Generation;
using Kiji.Rendering;
using Kiji.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kiji.Hosting;

/// <summary>The shared publish and development rendering pipeline. Lifetime belongs to the host.</summary>
internal sealed class SiteEngine
{
    private readonly ContentRuntime _runtime;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IReadOnlyList<PageParameterRegistration> _pageParameters;
    private readonly SiteArtifactRegistration[] _artifacts;
    private readonly IReadOnlyList<Assembly> _pageAssemblies;
    private readonly IReadOnlyCollection<Assembly> _serviceAssemblies;
    private readonly Type? _defaultLayoutType;
    private readonly Type? _notFoundComponentType;
    private readonly ResolvedSitePaths _options;
    private ComponentRenderer? _renderer;

    public SiteEngine(IServiceProvider services, SiteExecutionPaths paths, ResolvedSitePaths options,
        StaticSiteOptions siteOptions, ContentRuntime runtime, IHostEnvironment environment, IConfiguration configuration)
    {
        ServiceProvider = services;
        Paths = paths;
        _options = options;
        _runtime = runtime;
        _environment = environment;
        _configuration = configuration;
        _pageParameters = [.. services.GetServices<PageParameterRegistration>()];
        _artifacts = [.. services.GetServices<SiteArtifactRegistration>()];
        _pageAssemblies = [.. siteOptions.PageAssemblies];
        _serviceAssemblies = services.GetService<KijiServiceProviderStamp>()?.Assemblies ?? [];
        _defaultLayoutType = siteOptions.DefaultLayoutType;
        _notFoundComponentType = siteOptions.NotFoundComponentType;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        ContentRoots = [.. siteOptions.ContentPaths.Select(paths.ResolveAgainstRoot).Distinct(comparer)];
    }

    internal SiteInfo SiteInfo => ServiceProvider.GetRequiredService<SiteInfo>();
    internal SiteExecutionPaths Paths { get; }
    internal string[] ContentRoots { get; }
    internal IServiceProvider ServiceProvider { get; }
    private ILogger BuildLogger => field ??= ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(BuildLoggerExtensions.CategoryName);

    /// <summary>Generates the site, reusing unchanged output from a previous publish.</summary>
    /// <remarks>Repeated publication uses the same output directory; switching directories or execution modes requires a new site.</remarks>
    internal async Task PublishAsync(CancellationToken cancellationToken = default)
    {
        // The name may come from an inherited DOTNET_ENVIRONMENT, so show it before anything can fail.
        BuildLogger.PublishingEnvironment(_environment.EnvironmentName);

        await using var cacheLease = await CacheLease.AcquireAsync(Paths.ResolveCachePath(), cancellationToken);
        var options = _options;
        var assetManifest = options.AssetManifestPath is null ? new StaticAssetManifest { Assets = [] }
            : StaticAssetManifest.Load(options.AssetManifestPath, options.AssetEndpointsPath, SiteInfo.BaseUrl);
        UseAssetResources(assetManifest.Resources);
        var assets = assetManifest.Assets;
        OutputPathValidator.Validate(options, assetManifest, Paths.ProjectDirectory, Paths.ResolveKijiPath(), ContentRoots);
        InvalidateContent();
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var phases = new BuildPhaseTimer();
        var snapshot = CreateSnapshot();
        var renderer = GetRenderer();

        var reservedPaths = CreateReservedArtifactPaths(options, snapshot, assets);
        var artifactPaths = ReserveArtifactPaths(options, reservedPaths);
        phases.Mark(BuildPhaseTimer.Snapshot);

        // The sitemap reads only the page list, so it is written while pages are planned and
        // rendered. Artifacts that run site code, including the feed factory, still run after
        // all pages in registration order.
        var artifactContext = _artifacts.Length == 0 ? null : CreateOutputContext(snapshot);
        using var artifactCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var independentArtifacts = WriteArtifactsAsync(artifactPaths, artifactContext, independentOfPages: true,
            artifactCancellation.Token);

        IncrementalBuildPlanner planner;
        IncrementalBuildPlan plan;
        IReadOnlyList<string> staticFiles;
        IReadOnlyList<RenderedPage> rendered;
        var recorders = new ConcurrentDictionary<string, BuildDependencyRecorder>(StringComparer.OrdinalIgnoreCase);
        try
        {
            planner = new IncrementalBuildPlanner(
                options,
                Paths.ProjectDirectory,
                Paths.ResolveCachePath(),
                SiteInfo,
                _environment.EnvironmentName,
                _configuration,
                _runtime.Files, _runtime.Dependencies,
                ServiceProvider.GetRequiredService<IImageProcessor>(), assetManifest.Resources, BuildLogger,
                _runtime.ResolveItem);

            // Snapshot planning already required every page type to come from a page assembly.
            // Helpers and custom encoder factories may live outside the page assemblies.
            // Their code is a build input even when pages only reach it through injection.
            plan = await planner.CreatePlanAsync(
                snapshot.Pages,
                [.. _pageAssemblies, .. _serviceAssemblies, typeof(StaticSite).Assembly],
                cancellationToken);
            phases.Mark(BuildPhaseTimer.Plan);

            // The output directory is kept, never wiped: whatever this build does not
            // produce is deleted by the reconciliation pass below, which is both cheaper
            // than a delete-and-rewrite and checked against the directory itself.
            Directory.CreateDirectory(options.OutputDirectory);
            phases.Mark(BuildPhaseTimer.Clean);

            staticFiles = await planner.SyncStaticFilesAsync(assets);
            phases.Mark(BuildPhaseTimer.Static);

            // Render only the pages the plan could not prove unchanged, recording what
            // each render reads and writes for the next build's skip checks. The previous
            // manifest comes along so a render that reproduces the bytes already on disk
            // skips the write.
            rendered = await StaticSiteGenerator.RenderPagesAsync(
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
        }
        catch
        {
            // Report the build failure, not a write it interrupted.
            await artifactCancellation.CancelAsync();
            try { await independentArtifacts; }
            catch (Exception) when (artifactCancellation.IsCancellationRequested) { }
            throw;
        }

        await independentArtifacts;
        await WriteArtifactsAsync(artifactPaths, artifactContext, independentOfPages: false, cancellationToken);
        IReadOnlyList<string> artifacts = [.. artifactPaths.Select(path => Path.GetRelativePath(options.OutputDirectory, path))];
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
        // Still under the cache lease, so concurrent publishes cannot interleave the save.
        PublishJitProfile.Save(Paths.ResolveKijiPath(), rendered.Count);
        phases.Mark(BuildPhaseTimer.Manifest);

        var written = rendered.Count(static page => page.Written);
        BuildLogger.PagesGenerated(written, plan.CarriedPages.Count, rendered.Count - written, stopwatch.ElapsedMilliseconds);
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
        _ = SiteInfo;
        var options = _options;
        OutputPathValidator.ValidateInputs(options.OutputDirectory, ContentRoots);

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

        if (dependencies is not null)
        {
            foreach (var value in request.Parameters.Values)
            {
                if (value is not null && _runtime.ResolveItem(value) is { } identity)
                {
                    dependencies.AddValue(identity.DependencyKey, identity.Digest);
                }
            }
        }

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

    /// <summary>Resolves every artifact path, in registration order, before any output is written.</summary>
    private string[] ReserveArtifactPaths(ResolvedSitePaths options, Dictionary<string, string> reservedPaths)
    {
        var paths = new string[_artifacts.Length];
        for (var i = 0; i < _artifacts.Length; i++)
        {
            var artifact = _artifacts[i];
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
            paths[i] = fullPath;
        }

        return paths;
    }

    private async Task WriteArtifactsAsync(string[] paths, SiteOutputContext? context, bool independentOfPages,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < _artifacts.Length; i++)
        {
            var artifact = _artifacts[i];
            if (artifact.IndependentOfPages != independentOfPages)
            {
                continue;
            }

            if (independentOfPages)
            {
                // Leave the planning and rendering path before writing.
                await Task.Yield();
            }

            var fullPath = paths[i];
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await artifact.WriteFileAsync(fullPath, context!, cancellationToken);
            BuildLogger.ArtifactGenerated(fullPath);
        }
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

    private ComponentRenderer GetRenderer() => _renderer ??= new ComponentRenderer(ServiceProvider);
}
