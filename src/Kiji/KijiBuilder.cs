using Kiji.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji;

/// <summary>
/// Configures a Kiji site: site metadata, directory layout, services, and content sources.
/// Obtain an instance via <see cref="KijiApp.CreateBuilder"/>, then call <see cref="Build"/>.
/// </summary>
public sealed class KijiBuilder
{
    internal KijiBuilder(string[] args)
    {
        Args = args;
        Paths = new SitePaths(ResolveDefaultRoot());
        Runtime = new ContentRuntime();
    }

    /// <summary>
    /// Additional services available to Razor components (via <c>@inject</c>) and content loaders.
    /// </summary>
    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>
    /// The site directory layout.
    /// </summary>
    public SitePaths Paths { get; }

    /// <summary>
    /// The site metadata. Must be set before calling <see cref="Build"/>.
    /// </summary>
    public SiteInfo? Site { get; set; }

    internal string[] Args { get; }

    internal ContentRuntime Runtime { get; }

    internal List<KijiBuildInput> BuildInputs { get; } = [];

    /// <summary>
    /// Declares a file or directory (relative paths resolve against <see cref="SitePaths.Root"/>)
    /// whose content participates in the incremental build fingerprint. Use this for
    /// inputs Kiji cannot track itself — data files read by custom content loaders,
    /// templates, configuration — so changing them triggers a full re-render.
    /// </summary>
    public KijiBuilder AddBuildInput(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        BuildInputs.Add(new KijiBuildInput($"path:{path}", Value: null, Path: path));
        return this;
    }

    /// <summary>
    /// Declares a key/value pair participating in the incremental build fingerprint.
    /// Use this for untrackable inputs (e.g. data fetched over HTTP): pass a value that
    /// changes whenever the fetched data changes, and the build re-renders everything.
    /// </summary>
    public KijiBuilder AddBuildInput(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        BuildInputs.Add(new KijiBuildInput(key, value, Path: null));
        return this;
    }

    /// <summary>
    /// Registers a content source. The resulting <see cref="ContentDictionary{T}"/> is
    /// resolved by its element type, so there is no handle to pass around: components
    /// inject it, and route and feed factories resolve it from the services they are
    /// handed. Extension packages (e.g. markdown support) build on this method.
    /// </summary>
    /// <param name="loader">
    /// Loads the source items; invoked lazily once per site snapshot. It receives the
    /// app's services, so a dictionary can be derived from another one — resolve
    /// <see cref="ContentDictionary{T}"/> of the source type inside it.
    /// </param>
    /// <param name="key">Identifies each item. Keys must be non-empty and unique.</param>
    /// <param name="configure">Declares the dictionary's validation.</param>
    public KijiBuilder AddContentSource<T>(
        Func<IServiceProvider, IReadOnlyList<T>> loader,
        Func<T, string> key,
        Action<ContentSourceOptions<T>>? configure = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(key);

        var options = new ContentSourceOptions<T>();
        configure?.Invoke(options);

        return AddContentSource(
            services => new ContentSourceItems<T>(loader(services), Provenance: null),
            key,
            options);
    }

    /// <summary>
    /// The provenance-carrying form of <see cref="AddContentSource{T}(Func{IServiceProvider, IReadOnlyList{T}}, Func{T, string}, Action{ContentSourceOptions{T}})"/>,
    /// for loaders that project items into a model no longer implementing
    /// <see cref="IContentSourceFile"/> and must state the source file themselves.
    /// </summary>
    internal KijiBuilder AddContentSource<T>(
        Func<IServiceProvider, ContentSourceItems<T>> loader,
        Func<T, string> key,
        ContentSourceOptions<T> options,
        string contentSetScope = "")
        where T : class
    {
        Runtime.Register(new ContentDictionary<T>(Runtime, loader, key, options, contentSetScope));
        return this;
    }

    /// <summary>
    /// Builds the <see cref="KijiApp"/>. Declare page mappings on the returned app,
    /// then call <see cref="KijiApp.RunAsync"/>.
    /// </summary>
    public KijiApp Build()
    {
        if (Site is null)
        {
            throw new InvalidOperationException("KijiBuilder.Site must be set before calling Build().");
        }

        return new KijiApp(this);
    }

    /// <summary>
    /// Resolves the default site root: the site's own project directory when there is
    /// one, then the nearest repository root, then the current directory.
    /// </summary>
    /// <remarks>
    /// The project probe comes first because a site living inside a larger repository
    /// (docs alongside a library, one site among several) is its own root — resolving to
    /// the repository root would look for <c>contents/</c> in the wrong place. For a
    /// repository that is just one site, both probes land on the same directory.
    /// </remarks>
    private static string ResolveDefaultRoot()
    {
        return ResolveDefaultRoot(AppContext.BaseDirectory, Directory.GetCurrentDirectory());
    }

    internal static string ResolveDefaultRoot(string appBaseDirectory, string currentDirectory)
    {
        var projectRoot = FindNearestProjectDirectory(appBaseDirectory)
            ?? FindNearestProjectDirectory(currentDirectory);
        if (projectRoot is not null)
        {
            return projectRoot;
        }

        try
        {
            return SsgPathResolver.ResolveRepositoryRoot(appBaseDirectory, currentDirectory);
        }
        catch (DirectoryNotFoundException)
        {
            return currentDirectory;
        }
    }

    private static string? FindNearestProjectDirectory(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.csproj").Any() || directory.EnumerateFiles("*.fsproj").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
