using System.Reflection;
using Kiji.Hosting;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Kiji;

/// <summary>Registers content, pages and generated files on a Kiji host builder.</summary>
public static class StaticSiteBuilderExtensions
{
    /// <summary>Sets the required site metadata. A later call replaces it.</summary>
    /// <remarks>Metadata can be bound with Configuration.GetRequiredSection("Site").Get&lt;SiteInfo&gt;().</remarks>
    public static IHostApplicationBuilder UseSiteInfo(this IHostApplicationBuilder builder, SiteInfo siteInfo)
    {
        var context = SiteBuilderContext.Get(builder);
        ArgumentNullException.ThrowIfNull(siteInfo);
        // Configuration binding sets only keys present in the section, bypassing required members.
        if (siteInfo.BaseUrl is null || siteInfo.Name is null)
        {
            throw new ArgumentException(
                $"SiteInfo.{(siteInfo.BaseUrl is null ? nameof(SiteInfo.BaseUrl) : nameof(SiteInfo.Name))} is required. A SiteInfo bound from configuration lacks it when the section does not set it.",
                nameof(siteInfo));
        }
        builder.Services.RemoveAll<SiteInfo>();
        builder.Services.AddSingleton(siteInfo);
        context.RefreshSiteInfoRegistration();
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
        // Source identities use the project directory, including when a caller supplied
        // an absolute path inside the checkout, keeping those identities portable.
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

    /// <summary>Uses a default layout for pages without their own <c>@layout</c>.</summary>
    /// <remarks>Without a default layout, pages render directly inside the document body.</remarks>
    public static IHostApplicationBuilder UseDefaultLayout<TLayout>(this IHostApplicationBuilder builder)
        where TLayout : LayoutComponentBase
    {
        var context = SiteBuilderContext.Get(builder);
        context.Options.DefaultLayoutType = typeof(TLayout);
        return builder;
    }

    /// <summary>Adds an assembly whose public, non-abstract routed components are discovered as pages.</summary>
    /// <remarks>The entry assembly is included automatically. Repeated registration has no effect.</remarks>
    public static IHostApplicationBuilder AddPageAssembly(this IHostApplicationBuilder builder, Assembly assembly)
    {
        var context = SiteBuilderContext.Get(builder);
        ArgumentNullException.ThrowIfNull(assembly);

        if (!context.Options.PageAssemblies.Contains(assembly)) { context.Options.PageAssemblies.Add(assembly); }
        return builder;
    }

    /// <summary>Uses a routed page as <c>404.html</c> instead of at its own route.</summary>
    /// <remarks>The component must belong to a page assembly, declare exactly one route, and cannot also receive <c>AddPageParameters&lt;TPage&gt;</c>. A later call replaces the page.</remarks>
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

    /// <summary>Adds parameter sets rendered at every parameterized route of a page, or at every route when all are fixed.</summary>
    /// <param name="parameters">A factory run once per build; each object is a complete set of route values and component parameters.</param>
    /// <remarks>
    /// The page must belong to the entry assembly or an assembly added with AddPageAssembly. Fixed routes beside
    /// parameterized routes render once with default values. Registrations concatenate; an empty result generates no pages.
    /// Values keep their types and are treated as read-only. Values without a stable fingerprint disable HTML reuse.
    /// </remarks>
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

    /// <summary>Adds a site-wide output file written after all pages are rendered.</summary>
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
