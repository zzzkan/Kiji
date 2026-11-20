using System.Reflection;
using Kiji.Hosting;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kiji;

/// <summary>Configures a Kiji site's metadata, pages and output files.</summary>
public static class StaticSiteHostBuilderExtensions
{
    /// <summary>Sets the site metadata, which every site needs. A later call replaces it.</summary>
    public static IHostApplicationBuilder UseSiteInfo(this IHostApplicationBuilder builder, SiteInfo siteInfo)
    {
        var context = SiteBuilderContext.Get(builder);
        ArgumentNullException.ThrowIfNull(siteInfo);
        if (siteInfo.BaseUrl is null || siteInfo.Name is null)
        {
            throw new ArgumentException(
                $"SiteInfo.{(siteInfo.BaseUrl is null ? nameof(SiteInfo.BaseUrl) : nameof(SiteInfo.Name))} is required. A SiteInfo bound from configuration lacks it when the section does not set it.",
                nameof(siteInfo));
        }
        context.Options.SiteInfo = siteInfo;
        return builder;
    }

    internal static IHostApplicationBuilder RegisterContent<T>(
        this IHostApplicationBuilder builder,
        Func<IServiceProvider, IReadOnlyList<(string Id, T Item, string? Digest)>> loader,
        string contentSetScope = "",
        string? directory = null,
        string? serviceKey = null)
        where T : class
        => builder.RegisterContent<T>((services, _) => [.. loader(services)
            .Select(static entry => (entry.Id, entry.Item, entry.Digest, entry.Digest, (string?)null))],
            contentSetScope, directory, serviceKey);

    internal static IHostApplicationBuilder RegisterContent<T>(
        this IHostApplicationBuilder builder,
        Func<IServiceProvider, string, IReadOnlyList<(string Id, T Item, string? Digest, string? ProjectionDigest, string? FrontMatterDigest)>> loader,
        string contentSetScope = "",
        string? directory = null,
        string? serviceKey = null)
        where T : class
    {
        var context = SiteBuilderContext.Get(builder);
        var scope = directory is null
            ? contentSetScope
            : Path.GetRelativePath(Path.GetFullPath(context.Paths.ProjectDirectory), context.Paths.ResolveAgainstRoot(directory)).Replace('\\', '/');
        if (serviceKey is not null) { scope = $"{serviceKey.Length}:{serviceKey}:{scope}"; }
        var descriptor = context.Runtime.Register(serviceKey, () => new ContentCollection<T>(context.Runtime, loader, scope));
        builder.Services.Add(descriptor);
        context.ManagedServices.Add(descriptor);
        if (directory is not null) { context.Options.ContentPaths.Add(directory); }
        return builder;
    }

    /// <summary>Sets the layout of pages without their own <c>@layout</c>.</summary>
    public static IHostApplicationBuilder UseDefaultLayout<TLayout>(this IHostApplicationBuilder builder)
        where TLayout : LayoutComponentBase
    {
        var context = SiteBuilderContext.Get(builder);
        context.Options.DefaultLayoutType = typeof(TLayout);
        return builder;
    }

    /// <summary>Discovers pages in another assembly; the entry assembly is always included.</summary>
    public static IHostApplicationBuilder AddPageAssembly(this IHostApplicationBuilder builder, Assembly assembly)
    {
        var context = SiteBuilderContext.Get(builder);
        ArgumentNullException.ThrowIfNull(assembly);

        if (!context.Options.PageAssemblies.Contains(assembly)) { context.Options.PageAssemblies.Add(assembly); }
        return builder;
    }

    /// <summary>Renders a page with one route as <c>404.html</c> instead. A later call replaces it.</summary>
    public static IHostApplicationBuilder UseNotFoundPage<TPage>(this IHostApplicationBuilder builder)
        where TPage : IComponent
    {
        var context = SiteBuilderContext.Get(builder);
        if (builder.Services.Any(static descriptor => descriptor.ImplementationInstance is PageParameterRegistration registration
            && registration.ComponentType == typeof(TPage)))
        {
            throw new InvalidOperationException("A not-found page cannot also receive AddPageParameters<TPage>.");
        }
        context.Options.NotFoundComponentType = typeof(TPage);
        return builder;
    }

    /// <summary>Renders a page once per parameter set at its parameterized routes.</summary>
    /// <param name="parameters">Called once per build; each object sets route values and parameters by property name.</param>
    /// <remarks>Calls add sets. An empty result renders no pages. Values that cannot be fingerprinted render on every publish.</remarks>
    public static IHostApplicationBuilder AddPageParameters<TPage>(this IHostApplicationBuilder builder, Func<IServiceProvider, IEnumerable<object>> parameters)
        where TPage : IComponent
    {
        var context = SiteBuilderContext.Get(builder);
        ArgumentNullException.ThrowIfNull(parameters);

        if (context.Options.NotFoundComponentType == typeof(TPage))
        {
            throw new InvalidOperationException("A not-found page cannot also receive AddPageParameters<TPage>.");
        }

        builder.Services.AddSingleton(new PageParameterRegistration(typeof(TPage), parameters));
        return builder;
    }

    /// <summary>Adds a file written after all pages.</summary>
    public static IHostApplicationBuilder AddArtifact(
        this IHostApplicationBuilder builder,
        string outputRelativePath,
        Func<Stream, SiteOutputContext, CancellationToken, Task> write)
        => builder.AddArtifact(outputRelativePath, write, preserveUnchangedOutput: false);

    internal static IHostApplicationBuilder AddArtifact(
        this IHostApplicationBuilder builder,
        string outputRelativePath,
        Func<Stream, SiteOutputContext, CancellationToken, Task> write,
        bool preserveUnchangedOutput,
        bool independentOfPages = false)
    {
        var context = SiteBuilderContext.Get(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRelativePath);
        ArgumentNullException.ThrowIfNull(write);

        builder.Services.AddSingleton(new SiteArtifactRegistration(outputRelativePath, write, preserveUnchangedOutput, independentOfPages));
        return builder;
    }
}
