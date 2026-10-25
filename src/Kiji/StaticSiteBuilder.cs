using System.Reflection;
using Kiji.Assets;
using Kiji.Generation;
using Kiji.Hosting;
using Kiji.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        // Every configuration value is part of the publish fingerprint, so the default
        // sources are only those that travel with the project. Environment variables and
        // command-line arguments would differ between machines and between serve and publish.
        Configuration = new ConfigurationManager();
        // A site started through MSBuild always has its project directory. Without one, as
        // when tests only plan pages, there are no files to read and no base path to set.
        if (Directory.Exists(paths.ProjectDirectory))
        {
            // Relative paths in later AddJsonFile calls also resolve against the project.
            Configuration.SetBasePath(paths.ProjectDirectory);
            Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            Configuration.AddJsonFile($"appsettings.{settings.EnvironmentName}.json", optional: true, reloadOnChange: false);
        }
        if (HostEnvironment.IsDevelopment())
        {
            Configuration.AddUserSecrets(entryAssembly, optional: true, reloadOnChange: false);
        }

        // Defaults are registered first so that later registrations replace them. Managed
        // services are present from the start, so TryAdd-style extensions see them.
        ComponentRenderer.AddComponentRenderingServices(_services);
        Logging = new SiteLoggingBuilder(_services);
        Logging.AddConfiguration(Configuration.GetSection("Logging"));
        _services.AddSingleton(_ => SiteInfo);
        _services.AddSingleton<IHostEnvironment>(HostEnvironment);
        _services.AddSingleton<IConfiguration>(Configuration);
        _services.AddOptions<ImageProcessorOptions>();
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ImageProcessorOptions>, ImageProcessorOptionsValidator>());
        _services.AddSingleton<IImageProcessor>(static provider =>
            new ImageProcessor(provider.GetRequiredService<IOptions<ImageProcessorOptions>>().Value));
        _services.AddSingleton<ContentFileRegistry>();
        _managedServices = [.. _services.Where(static descriptor => SiteServiceRegistration.IsManaged(descriptor.ServiceType))];
    }

    /// <summary>The site metadata, required before <see cref="Build"/>.</summary>
    /// <remarks>
    /// It can be bound from configuration, for example
    /// <c>builder.Configuration.GetRequiredSection("Site").Get&lt;SiteInfo&gt;()</c>.
    /// </remarks>
    /// <exception cref="ArgumentException">The value has no <see cref="Kiji.SiteInfo.BaseUrl"/> or <see cref="Kiji.SiteInfo.Name"/>.</exception>
    public SiteInfo SiteInfo
    {
        get => field ?? throw new InvalidOperationException("Set StaticSiteBuilder.SiteInfo before building the site.");
        set
        {
            EnsureConfigurable();
            ArgumentNullException.ThrowIfNull(value);
            // Configuration binding sets only the keys present, bypassing required members.
            if (value.BaseUrl is null || value.Name is null)
            {
                throw new ArgumentException(
                    $"SiteInfo.{(value.BaseUrl is null ? nameof(SiteInfo.BaseUrl) : nameof(SiteInfo.Name))} is required. A SiteInfo bound from configuration lacks it when the section does not set it.",
                    nameof(value));
            }
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

    /// <summary>The site configuration, also available to components as <see cref="IConfiguration"/>.</summary>
    /// <remarks>
    /// It reads <c>appsettings.json</c> and <c>appsettings.{Environment}.json</c> from the project directory, and user
    /// secrets in Development. Add other sources, such as prefixed environment variables, explicitly. Every value is
    /// part of the publish fingerprint: changing one renders every page again. Files are read once per process.
    /// </remarks>
    public ConfigurationManager Configuration { get; }

    /// <summary>The logging configuration for components and services.</summary>
    /// <remarks>
    /// Kiji writes Warning and higher to the console, using standard error so that <c>dotnet publish</c> shows it.
    /// The <c>Logging</c> configuration section applies, as in other .NET hosts.
    /// </remarks>
    public ILoggingBuilder Logging { get; }

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

    /// <summary>Uses a routed page as <c>404.html</c> instead of at its own route.</summary>
    /// <remarks>The component must belong to a page assembly, declare exactly one route, and cannot also receive <c>AddPageParameters&lt;TPage&gt;</c>. A later call replaces the page.</remarks>
    public StaticSiteBuilder UseNotFoundPage<TPage>()
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
        _ = SiteInfo;
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
