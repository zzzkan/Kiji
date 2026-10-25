using System.Reflection;
using Kiji.Assets;
using Kiji.Generation;
using Kiji.Hosting;
using Kiji.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kiji;

/// <summary>Configures a static site's metadata, services, content, pages, and generated files.</summary>
/// <remarks>
/// Create it with <see cref="StaticSite.CreateBuilder(string[])"/> and call <see cref="Build"/> once.
/// The builder and <see cref="Services"/> become read-only when the site is built.
/// </remarks>
public sealed class StaticSiteBuilder
{
    private readonly ServiceCollection _services = [];
    private readonly List<ServiceDescriptor> _managedServices;
    private readonly List<string> _contentPaths = [];
    private readonly List<PageParameterRegistration> _pageParameters = [];
    private readonly List<SiteArtifactRegistration> _artifacts = [];
    private readonly List<Assembly> _pageAssemblies = [];

    private bool _built;

    internal StaticSiteBuilder(string[] args, SiteExecutionPaths paths, Assembly entryAssembly, SiteRunSettings settings)
    {
        Args = [.. args];
        Paths = paths;
        RunSettings = settings;
        _pageAssemblies.Add(entryAssembly);
        HostEnvironment = new SiteHostEnvironment(
            settings.EnvironmentName, entryAssembly.GetName().Name ?? string.Empty, paths.ProjectDirectory);

        // Defaults are registered first so that later registrations replace them. Managed
        // services are present from the start, so TryAdd-style extensions see them.
        ComponentRenderer.AddComponentRenderingServices(_services);
        _services.AddSingleton(_ => Info);
        _services.AddSingleton<IHostEnvironment>(HostEnvironment);
        _services.AddOptions<ImageProcessorOptions>();
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ImageProcessorOptions>, ImageProcessorOptionsValidator>());
        _services.AddSingleton<IImageProcessor>(static provider =>
            new ImageProcessor(provider.GetRequiredService<IOptions<ImageProcessorOptions>>().Value));
        _services.AddSingleton<ContentFileRegistry>();
        _managedServices = [.. _services.Where(static descriptor => SiteServiceRegistration.IsManaged(descriptor.ServiceType))];
    }

    /// <summary>The site metadata, required before <see cref="Build"/>.</summary>
    public SiteInfo Info
    {
        get => field ?? throw new InvalidOperationException("Set StaticSiteBuilder.Info before building the site.");
        set
        {
            EnsureConfigurable();
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = null!;

    /// <summary>The services available to components and site factories.</summary>
    /// <remarks>
    /// The collection becomes read-only when the site is built. Each page render owns a scope.
    /// Content loaders, page parameter and feed factories, and artifacts cannot resolve scoped services.
    /// Kiji's managed services cannot be changed. Use AlwaysRender on components reading untracked external state.
    /// </remarks>
    public IServiceCollection Services => _services;

    /// <summary>The environment the site runs in, also available to components as <see cref="IHostEnvironment"/>.</summary>
    /// <remarks>
    /// The name comes from <c>DOTNET_ENVIRONMENT</c>; otherwise it is <c>Production</c> during
    /// <c>dotnet publish</c> and <c>Development</c> while serving. The content root is the project directory.
    /// </remarks>
    public IHostEnvironment Environment => HostEnvironment;

    internal string[] Args { get; }

    internal SiteExecutionPaths Paths { get; }

    internal SiteRunSettings RunSettings { get; }

    internal ContentRuntime Runtime { get; } = new();

    internal SiteHostEnvironment HostEnvironment { get; }

    internal IReadOnlyList<string> ContentPaths => _contentPaths;

    internal IReadOnlyList<PageParameterRegistration> PageParameters => _pageParameters;

    internal IReadOnlyList<SiteArtifactRegistration> Artifacts => _artifacts;

    internal IReadOnlyList<Assembly> PageAssemblies => _pageAssemblies;

    internal Type? DefaultLayoutType { get; private set; }

    internal Type? NotFoundComponentType { get; private set; }

    /// <summary>Adds a content source resolved as <see cref="ContentDictionary{T}"/>.</summary>
    /// <param name="loader">Loads items lazily once per snapshot, after execution paths are settled.</param>
    public StaticSiteBuilder AddContentSource<T>(Func<IServiceProvider, IReadOnlyList<T>> loader)
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

    /// <summary>Adds a content source with stable identities and explicit data digests.</summary>
    public StaticSiteBuilder AddContentSource<T>(string sourceId, Func<IServiceProvider, IReadOnlyList<ContentEntry<T>>> loader)
        where T : class
    {
        EnsureConfigurable();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(loader);
        return RegisterContent<T>(services => [.. loader(services)
            .Select(static entry => (entry.Id, entry.Value, entry.Digest))], sourceId);
    }

    internal StaticSiteBuilder RegisterContent<T>(
        Func<IServiceProvider, IReadOnlyList<(string Key, T Item, string? Digest)>> loader,
        string contentSetScope = "",
        string? directory = null)
        where T : class
    {
        EnsureConfigurable();
        // Source identities use the project directory, including when a caller supplied
        // an absolute path inside the checkout, keeping those identities portable.
        var descriptor = Runtime.Register(() => new ContentDictionary<T>(Runtime, loader, directory is null
            ? contentSetScope
            : Path.GetRelativePath(Path.GetFullPath(Paths.ProjectDirectory), Paths.ResolveAgainstRoot(directory)).Replace('\\', '/')));
        Services.Add(descriptor);
        _managedServices.Add(descriptor);
        if (directory is not null) { _contentPaths.Add(directory); }
        return this;
    }

    /// <summary>Uses a default layout for pages without their own <c>@layout</c>.</summary>
    /// <remarks>Without a default layout, pages render directly inside the document body.</remarks>
    public StaticSiteBuilder UseDefaultLayout<TLayout>()
        where TLayout : LayoutComponentBase
    {
        EnsureConfigurable();
        DefaultLayoutType = typeof(TLayout);
        return this;
    }

    /// <summary>Adds an assembly whose public, non-abstract routed components are discovered as pages.</summary>
    /// <remarks>The entry assembly is included automatically. Repeated registration has no effect.</remarks>
    public StaticSiteBuilder AddPageAssembly(Assembly assembly)
    {
        EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(assembly);

        if (!_pageAssemblies.Contains(assembly)) { _pageAssemblies.Add(assembly); }
        return this;
    }

    /// <summary>Adds a page written as <c>404.html</c>.</summary>
    /// <remarks>The component must belong to a page assembly, declare exactly one route, and cannot also receive <c>AddPageParameters&lt;TPage&gt;</c>.</remarks>
    public StaticSiteBuilder AddNotFoundPage<TPage>()
        where TPage : IComponent
    {
        EnsureConfigurable();
        if (_pageParameters.Any(static registration => registration.ComponentType == typeof(TPage)))
        {
            throw new InvalidOperationException("A not-found page cannot also receive AddPageParameters<TPage>.");
        }
        NotFoundComponentType = typeof(TPage);
        return this;
    }

    /// <summary>Adds parameter sets rendered at every parameterized route of a page, or at every route when all are fixed.</summary>
    /// <param name="parameters">A factory run once per build; each object is a complete set of route values and component parameters.</param>
    /// <remarks>
    /// The page must belong to the entry assembly or an assembly added with AddPageAssembly. Fixed routes beside
    /// parameterized routes render once with default values. Registrations concatenate; an empty result generates no pages.
    /// Values keep their types and are treated as read-only. Values without a stable fingerprint disable HTML reuse.
    /// </remarks>
    public StaticSiteBuilder AddPageParameters<TPage>(Func<IServiceProvider, IEnumerable<object>> parameters)
        where TPage : IComponent
    {
        EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(parameters);

        if (NotFoundComponentType == typeof(TPage))
        {
            throw new InvalidOperationException("A not-found page cannot also receive AddPageParameters<TPage>.");
        }

        _pageParameters.Add(new PageParameterRegistration(typeof(TPage), parameters));
        return this;
    }

    /// <summary>Adds a site-wide output file written after all pages are rendered.</summary>
    public StaticSiteBuilder AddArtifact(
        string outputRelativePath,
        Func<Stream, SiteOutputContext, CancellationToken, Task> write)
        => AddArtifact(outputRelativePath, write, preserveUnchangedOutput: false);

    internal StaticSiteBuilder AddArtifact(
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

    /// <summary>Builds the site, validating its services. Call it once.</summary>
    /// <remarks>Content is not loaded until the site runs.</remarks>
    public StaticSite Build()
    {
        if (_built)
        {
            throw new InvalidOperationException("Build can only be called once.");
        }
        _ = Info;
        _built = true;
        HostEnvironment.Freeze();
        return new StaticSite(this);
    }

    /// <param name="resolvePaths">Returns the execution paths once the site has settled them.</param>
    internal (ServiceProvider Services, IReadOnlyCollection<Assembly> Assemblies) BuildServices(Func<ResolvedSitePaths> resolvePaths)
    {
        SiteServiceRegistration.Validate(_services, _managedServices);
        // A default here would pin the singleton to the wrong execution mode.
        _services.AddSingleton(_ => resolvePaths());
        _services.MakeReadOnly();

        var assemblies = SiteServiceRegistration.CollectAssemblies(_services);
        var provider = _services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        try
        {
            // The generic host's ValidateOnStart needs a started host, so Build reads the
            // settings itself: invalid values fail here rather than midway through a publish.
            _ = provider.GetRequiredService<IOptions<ImageProcessorOptions>>().Value;
        }
        catch
        {
            provider.Dispose();
            throw;
        }
        return (provider, assemblies);
    }

    internal void EnsureConfigurable()
    {
        if (_built)
        {
            throw new InvalidOperationException("The site has already been built. Configure the builder before calling Build.");
        }
    }
}
