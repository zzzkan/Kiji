---
title: API reference
description: The site-authoring API, its defaults, and when each registration runs.
order: 100
---

This page lists the public surface used to author a Kiji site. Registrations return the
same `StaticSite`, so they may be chained. Configure the site before `RunAsync`; execution
freezes the configuration.

## Site lifecycle

```csharp
var app = StaticSite.Create(args);
app.Info = new SiteInfo { BaseUrl = new Uri("https://example.com/"), Name = "My Site" };
// Add* and Use* registrations
return await app.RunAsync();
```

| Member                             | Purpose                                                                                                   |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `StaticSite.Create(string[] args)` | Creates one site. Arguments are forwarded to ASP.NET Core configuration while serving.                    |
| `Info`                             | Required site metadata. Assign it before any operation that starts execution.                             |
| `Paths`                            | Input paths, mutable until execution starts.                                                              |
| `RunAsync(CancellationToken)`      | Serves during `dotnet run` or `dotnet watch`, and generates during `dotnet publish`. A site can run once. |

## SiteInfo and paths

`SiteInfo.BaseUrl` and `Name` are required. `BaseUrl` must be an absolute HTTP or HTTPS
URL without a query or fragment; Kiji normalizes it to a trailing slash.

| Property                     | Default                                                | Used for                                               |
| ---------------------------- | ------------------------------------------------------ | ------------------------------------------------------ |
| `SiteInfo.Description`       | Empty                                                  | Site-defined metadata and feed description             |
| `SiteInfo.Language`          | `"en"`                                                 | The generated `<html lang>` attribute and RSS language |
| `SiteInfo.Author`            | Empty                                                  | Site-defined metadata                                  |
| `SitePaths.RootDirectory`    | Nearest project, then Git root, then current directory | Resolving all relative site paths                      |
| `SitePaths.ContentDirectory` | `contents`                                             | Content sources                                        |
| `SitePaths.StaticDirectory`  | `wwwroot` when null                                    | Files copied unchanged to the output                   |

## Pages and layouts

| Registration                  | Contract                                                                                                                            |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `AddStaticPages()`            | Finds public, non-abstract components with fixed routes in the entry assembly.                                                      |
| `AddStaticPages(Assembly)`    | Finds fixed routes in another assembly. Repeating the same assembly has no effect.                                                  |
| `AddPages<TPage>(factory)`    | Registers parameter sets for a component with exactly one parameterized route. The factory runs once per registration per snapshot. |
| `UseDefaultLayout<TLayout>()` | Applies a `LayoutComponentBase` to pages without their own `@layout`.                                                               |
| `UseNotFoundPage<TPage>()`    | Writes the routed component as `/404.html`; it cannot also be registered with `AddPages`.                                           |
| `AddPageService<T>()`         | Creates one concrete service instance per page render and disposes it afterward.                                                    |

`AddPages` accepts anonymous objects or string/object dictionaries. Names match route and
component parameters case-insensitively. Values keep their .NET types; Kiji performs no
implicit parameter conversion. See [Routing](../concepts/#routing) for binding and
incremental-build rules.

Use `Kiji.Components.HeadContent` once per page to supply `<title>`, metadata, and links to
the generated document head. If several instances render, the last one wins.

## Content

```csharp
app.UseContentSource<Author>(services => LoadAuthors());
```

`UseContentSource<T>(Func<IServiceProvider, IReadOnlyList<T>>)` registers one lazily
materialized `ContentDictionary<T>`. A dictionary is identified by `T`, so the same element
type cannot be registered twice. General content receives zero-based string keys in loader
order. Resolve it from component injection or from the service provider handed to a route,
feed, loader, or artifact factory.

`ContentDictionary<T>` implements `IReadOnlyDictionary<string,T>`. An indexer lookup records
a dependency on one file-backed item; enumerating keys, values, or entries records a
dependency on the collection.

### Markdown

```csharp
app.UseMarkdownContent<PostFrontMatter>();
app.UseMarkdownContent<PostFrontMatter, Post>(Post.Create, options =>
{
    options.Directory = "posts";
    options.FileFilter = file => !file.Name.StartsWith('_');
});
```

| Member                                                        | Purpose                                                                               |
| ------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `UseMarkdownContent<TFrontMatter>(configure?)`                | Registers `ContentDictionary<MarkdownContent<TFrontMatter>>`.                         |
| `UseMarkdownContent<TFrontMatter,TModel>(select, configure?)` | Projects each file independently into a site model.                                   |
| `MarkdownOptions.Directory`                                   | Limits the recursive scan to a directory below `contents`.                            |
| `MarkdownOptions.FileFilter`                                  | Includes or excludes files before loading.                                            |
| `ConfigureMarkdown`                                           | Extends the default Markdig pipeline.                                                 |
| `ConfigureYaml`                                               | Extends the camel-case, ignore-unknown-properties YAML deserializer.                  |
| `AddHtmlPostProcessor`                                        | Applies a synchronous HTML transform after Markdown rendering, in registration order. |
| `MarkdownContent<T>.FileInfo`                                 | Source-file metadata.                                                                 |
| `MarkdownContent<T>.FrontMatter`                              | Parsed front matter.                                                                  |
| `MarkdownContent<T>.RenderAsync()`                            | Renders HTML and materializes referenced image variants beside the current page.      |

See [Markdown and images](../markdown/) for examples and page-bundle URL behavior.

## Build inputs

| Registration                | Purpose                                                                     |
| --------------------------- | --------------------------------------------------------------------------- |
| `AddBuildInput(path)`       | Declares an external file or directory. Relative paths use `RootDirectory`. |
| `AddBuildInput(key, value)` | Declares a named value such as a remote-data version or encoder setting.    |

A changed declared input requires a full rebuild. In development, declared paths are also
watched for content invalidation and browser reload. See [Performance](../performance/).

## Feeds, sitemaps, and artifacts

```csharp
app.AddRssFeed(services => GetItems(services), path: "feed.xml");
app.AddSitemap(path: "sitemap.xml", excludedPaths: ["preview/"]);
```

`AddRssFeed` preserves the supplied `FeedItem` order. A `FeedItem` contains `Title`,
`Description`, `PublishedAt`, and a prefix-free `RelativePath` combined with `BaseUrl`.

`AddSitemap` includes generated pages and always excludes `404.html`. Additional exclusions
match site-relative page paths exactly.

`AddArtifact(outputRelativePath, write)` registers an output written after all pages. Its
writer receives the output stream, a `SiteOutputContext`, and a cancellation token.
`SiteOutputContext` exposes `Site`, all generated `Pages`, and `Services`. Each
`SitePageInfo` exposes its URL-facing `RelativePath` and file-facing `OutputRelativePath`.

## Image processing

```csharp
app.UseImageProcessor(() => new CustomProcessor());
```

The factory is lazy; its last registration wins. Kiji owns and disposes the returned
processor. One instance is shared across concurrent renders and content reloads.

A custom processor implements:

```csharp
Task<ProcessedImageInfo> ProcessAsync(
    string sourceFilePath,
    string outputDirectory,
    string? cacheDirectory = null,
    CancellationToken cancellationToken = default);
```

Return the original pixel dimensions and `ImageVariant` entries in ascending width order.
Each variant contains a file name relative to `outputDirectory` and its pixel width. The
processor must write those files, support concurrent calls, and retain no page or content
state. Declare external settings with `AddBuildInput` and include settings and encoder
versions in any persistent cache identity.
