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
    /// Registers a content source and returns its typed collection handle.
    /// Extension packages (e.g. markdown support) build on this method.
    /// </summary>
    /// <param name="loader">Loads the source items; invoked lazily once per site snapshot.</param>
    public ContentCollection<T> AddContentSource<T>(Func<IServiceProvider, IReadOnlyList<T>> loader)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(loader);

        return new ContentCollection<T>(Runtime, loader);
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

    private static string ResolveDefaultRoot()
    {
        try
        {
            return SsgPathResolver.ResolveRepositoryRoot(AppContext.BaseDirectory, Directory.GetCurrentDirectory());
        }
        catch (DirectoryNotFoundException)
        {
            return Directory.GetCurrentDirectory();
        }
    }
}
