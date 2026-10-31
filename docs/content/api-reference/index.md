---
title: API reference
description: Configure pages, content, images, and generated files.
order: 100
---

Configure the site on `StaticSiteBuilder`, then build and run it. Registration methods return
the builder, so calls can be chained. `Program.cs` examples assume `using Kiji;`,
`using Microsoft.Extensions.DependencyInjection;`, and a `builder` from
`StaticSite.CreateBuilder(args)`.

- [Site setup](#site-setup)
- [Pages and rendering](#pages-and-rendering)
- [Content and images](#content-and-images)
- [Page reuse controls](#page-reuse-controls)
- [Generated files](#generated-files)
- [Unsupported Blazor features](#unsupported-blazor-features)

## Site setup

### StaticSiteBuilder and StaticSite

```csharp
var builder = StaticSite.CreateBuilder(args);
builder.SiteInfo = new SiteInfo
{
    BaseUrl = new Uri("https://example.com/"),
    Name = "My Site",
};

// builder.Services, Add* and Use* registrations

return await builder.Build().RunAsync();
```

| Member                                                                         | Behavior                                                                                                                                                                    |
| ------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `static StaticSiteBuilder StaticSite.CreateBuilder(string[] args)`             | Creates a builder for the current project. While serving, `--environment` in `args` selects the environment, and `args` go to ASP.NET Core configuration, such as `--urls`. |
| `SiteInfo StaticSiteBuilder.SiteInfo`                                          | Required site metadata.                                                                                                                                                     |
| `IServiceCollection StaticSiteBuilder.Services`                                | See [Services](#services).                                                                                                                                                  |
| `IHostEnvironment StaticSiteBuilder.Environment`                               | See [Environment](#environment).                                                                                                                                            |
| `IConfigurationManager StaticSiteBuilder.Configuration`                        | See [Configuration](#configuration).                                                                                                                                        |
| `ILoggingBuilder StaticSiteBuilder.Logging`                                    | See [Logging](#logging).                                                                                                                                                    |
| `StaticSite StaticSiteBuilder.Build()`                                         | Validates the services and makes them read-only. Call it once.                                                                                                              |
| `Task<int> StaticSite.RunAsync(CancellationToken cancellationToken = default)` | Serves during `dotnet watch` or generates the site during `dotnet publish`, then disposes the site. Return its exit code.                                                   |

### Environment

`builder.Environment` is a standard
[`IHostEnvironment`](https://learn.microsoft.com/dotnet/core/extensions/generic-host#ihostenvironment),
also injectable into components. `IsDevelopment()` needs `using Microsoft.Extensions.Hosting;`:

```csharp
if (builder.Environment.IsDevelopment())
{
    // For example, also list draft posts while serving.
}
```

During `dotnet publish`, the name comes only from `DOTNET_ENVIRONMENT` and defaults to
`Production`. While serving, `--environment` comes first, then `DOTNET_ENVIRONMENT`, then
`ASPNETCORE_ENVIRONMENT`, and the default is `Development`. The name is read-only, and
`ContentRootPath` is the project directory. Changing the name renders every page again on
the next publish.

### Configuration

`builder.Configuration` is a standard
[configuration](https://learn.microsoft.com/dotnet/core/extensions/configuration), also
injectable as `IConfiguration`. It reads two files from the project directory, the second
overriding the first:

1. `appsettings.json`
2. `appsettings.{Environment}.json`

```csharp
builder.Services.Configure<ImageProcessorOptions>(builder.Configuration.GetSection("Images"));
```

- Environment variables, command-line arguments, and user secrets are not read, because they
  differ between machines. Add a source explicitly when you need one:
  `builder.Configuration.AddEnvironmentVariables("MYSITE_")`.
- Changing any value outside the `Logging` section renders every page again on the next
  publish.
- Files are read once. Restart `dotnet watch` after editing them.
- The development server reads its own settings, such as `urls`, through standard
  ASP.NET Core configuration, including the project's appsettings files, `--urls`, and
  `ASPNETCORE_URLS`.

### Logging

`builder.Logging` is a standard
[`ILoggingBuilder`](https://learn.microsoft.com/dotnet/core/extensions/logging), and the
`Logging` configuration section applies as in other .NET hosts. The console shows `Warning`
and higher, and Kiji's summary in the `Kiji.Build` category. Kiji's default logger reports
exceptions caught by
[`ErrorBoundary`](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/handle-errors?view=aspnetcore-10.0#error-boundaries)
as warnings, so a deliberate fallback publishes with its message logged. `Debug` adds a
message for each page and generated file:

```json
{ "Logging": { "LogLevel": { "Kiji.Build": "Debug" } } }
```

- Logs never fail a publish, whatever their level. A publish fails when an exception ends
  `RunAsync`, or when it is canceled, which returns exit code 1. To stop a publish, throw
  an exception that no error boundary catches.
- During `dotnet publish`, log messages are MSBuild messages, not warnings or errors. CI logs
  and `--tl:off` show them. In a terminal, add `-v d`, also to see why a publish failed.
  `-v q` hides them.
- Cached pages do not replay their messages.
- Error boundaries [prevent HTML reuse](../incremental-builds/), even while healthy.
- The development server's ASP.NET Core messages use the same settings.

### SiteInfo

| Property             | Default  | Behavior                                                                                                                 |
| -------------------- | -------- | ------------------------------------------------------------------------------------------------------------------------ |
| `Uri BaseUrl`        | Required | Absolute HTTP or HTTPS URL without a query or fragment. Its path, with `/` appended if missing, is the site's base path. |
| `string Name`        | Required | Site name, also the RSS channel title. Must not be blank.                                                                |
| `string Description` | `""`     | Site description, also the RSS channel description.                                                                      |
| `string Language`    | `"en"`   | Written to `<html lang>` and the RSS channel.                                                                            |
| `string Author`      | `""`     | For your components; Kiji does not write it.                                                                             |

`ResolveUrl(string path)` returns the absolute URL of a site-relative path. A leading `/`
is ignored, so `"about/"` and `"/about/"` both stay under the base path, and `""` returns
`BaseUrl`. External URLs and `..` are rejected.

## Pages and rendering

### Pages

Kiji discovers `@page` routes on public, non-abstract components in the entry assembly.
See [Routing](../routing/) for examples.

| Method                                                                             | Behavior                                                                                                                          |
| ---------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `AddPageAssembly(Assembly assembly)`                                               | Also discovers routed components in another assembly.                                                                             |
| `AddPageParameters<TPage>(Func<IServiceProvider, IEnumerable<object>> parameters)` | Adds parameter sets for a component. The factory runs once per build or reload and can read content.                              |
| `UseNotFoundPage<TPage>()`                                                         | Writes the component as `404.html`. It needs exactly one route and cannot also receive page parameters. A later call replaces it. |

Page parameter sets follow these rules:

- Each set is an anonymous object or a string-keyed dictionary. Names match public
  writable `[Parameter]` properties, ignoring case. Values keep their types; route values
  are formatted with the invariant culture.
- Each set is rendered at every parameterized route of the component. Fixed routes beside
  them render once with default values; a component with only fixed routes renders once
  per set instead.
- Omitted parameters keep their defaults; `EditorRequired` is not checked.
- Unknown names, wrong types, missing or empty route values, and duplicate output paths
  fail before rendering.
- Calls for the same component add up; an empty sequence generates no pages.

A page's HTML can be reused only when every parameter value is `null`, a `string`, `bool`,
`char`, an 8- to 64-bit integer, an enum, or a `Guid`.

### Layout

`UseDefaultLayout<TLayout>()` sets the layout for pages without their own `@layout`.
Without it, pages render directly inside `<body>`.

### Services

`builder.Services` registers services with standard
[.NET dependency injection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview):

```csharp
builder.Services.AddScoped<RelatedPosts>();
```

- Register services before `Build`, which reports missing dependencies.
- Options with `ValidateOnStart()` are validated when the site starts, before any output.
- Each page render has its own scope, shared by its page, layout, and child components.
- Content loaders, page parameter and feed factories, and artifact writers cannot resolve
  scoped services.
- `SiteInfo`, `PageInfo`, `IHostEnvironment`, `IConfiguration`, and `ContentCollection<T>` come
  from Kiji and cannot be replaced.

### Page metadata

Inject `PageInfo` for the current page's public URL, including the base path:

```razor
@inject PageInfo Page

<HeadContent>
    <link rel="canonical" href="@Page.Url.AbsoluteUri" />
</HeadContent>
```

#### PageTitle, HeadContent, and HeadOutlet

Use Blazor's `PageTitle` and `HeadContent`; Kiji's `HeadOutlet` writes them into `<head>`
after asynchronous rendering finishes. Separate `HeadContent` blocks replace each other.
See [Layouts and the document head](../concepts/#layouts-and-the-document-head).

### Static assets

`Assets["css/app.css"]`, inherited from `ComponentBase`, returns the asset's public URL
with the base path and, when available, a fingerprint. Unknown paths are returned
unchanged. URLs written directly in CSS, JavaScript, or HTML are not rewritten.

Place Blazor's `<ImportMap />` in `HeadContent` before scripts that use JavaScript imports.
Its `ImportMapDefinition` parameter accepts a custom map. See the
[ImportMap component](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0#importmap-component).

## Content and images

### Content collections

`ContentCollection<T>` is an `IReadOnlyCollection<T>` resolved through dependency injection.
Enumeration returns items in source order. Treat items as immutable. Markdown IDs are paths
relative to the content directory, using `/` and including `.md`; they do not determine URLs.
IDs ignore case. Reads are tracked as [page dependencies](../incremental-builds/#page-dependencies).

| Member | Behavior |
| --- | --- |
| `int Count` | Number of items; observes the collection. |
| `GetEnumerator()` | Enumerates items in source order; observes the collection. |
| `T this[string id]` | Looks up an item; throws `KeyNotFoundException` if absent. |
| `bool TryGetValue(string id, out T? item)` | Looks up an item, or returns `false`. |
| `bool Contains(string id)` | Tests whether the item exists. |

An ID lookup observes that item; a missing ID observes the collection. There is no public
constructor. Register Markdown sources with the methods below. Compute derived data while
rendering, or share it through a scoped service registered with `builder.Services.AddScoped<T>()`.

### Markdown

Import `Kiji.Markdown`. See [Markdown and images](../markdown/) for examples.

| Method                                                                                                                                                | Behavior                                                                             |
| ----------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `AddMarkdownContent<TFrontMatter>(Action<MarkdownContentOptions>? configure = null)`                                                                  | Loads `*.md` files and registers `ContentCollection<MarkdownContent<TFrontMatter>>`. |
| `AddMarkdownContent<TFrontMatter,TModel>(Func<MarkdownContent<TFrontMatter>,TModel> select, Action<MarkdownContentOptions>? configure = null)`        | Converts each file with `select` and registers `ContentCollection<TModel>`.          |

Keyed overloads register the same collection types under a string service key:

| Method | Behavior |
| --- | --- |
| `AddKeyedMarkdownContent<TFrontMatter>(string serviceKey, Action<MarkdownContentOptions>? configure = null)` | Registers keyed Markdown content. |
| `AddKeyedMarkdownContent<TFrontMatter,TModel>(string serviceKey, Func<MarkdownContent<TFrontMatter>,TModel> select, Action<MarkdownContentOptions>? configure = null)` | Registers keyed projected models. |

Resolve them with `services.GetRequiredKeyedService<ContentCollection<T>>(serviceKey)`.
The element type and service key identify a registration; duplicate pairs fail. An unkeyed
collection can coexist with keyed collections of the same type. Service keys must not be
empty or whitespace. Source options are evaluated immediately during registration.

| `MarkdownContentOptions` member   | Behavior                                                                     |
| --------------------------------- | ---------------------------------------------------------------------------- |
| `string Directory`                | Directory to load, absolute or relative to the project. Default `"content"`. |
| `Func<FileInfo,bool>? FileFilter` | Returns `false` to skip a file. Default: load every `*.md` file.             |

Rendering settings apply to every source. Configure them with
`builder.Services.Configure<MarkdownOptions>(options => ...)`.

| `MarkdownOptions` member           | Behavior                                                                                                                                                                                                                   |
| ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `MarkdownPipelineBuilder Pipeline` | Markdig builder. Defaults: GFM-style tables, task lists, autolinks and strikethrough; footnotes, GitHub heading IDs and generic attributes. Use `Extensions.Replace` to change a registered extension and `DocumentProcessed` to change the parsed document. |
| `DeserializerBuilder Yaml`         | YamlDotNet builder. Defaults: camelCase matched case-insensitively, errors on unknown keys, and UTC for dates without an offset.                                                                                           |

| `MarkdownContent<TFrontMatter>` member                                         | Behavior                                                                            |
| ------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------- |
| `string Path`                                                                  | The path relative to the content directory, using `/`. Also the collection ID.     |
| `TFrontMatter FrontMatter`                                                     | The parsed front matter.                                                            |
| `ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)` | Renders the body to HTML and writes its local images beside the current page.       |

Call `RenderAsync` while rendering a page, because local images are written beside that
page. `<MarkdownBody Content="@content" />` renders the body with no wrapper element.

### Image settings

Configure the default processor with `ImageProcessorOptions` from `Kiji.Assets`, using the
[options pattern](https://learn.microsoft.com/dotnet/core/extensions/options):

```csharp
builder.Services.Configure<ImageProcessorOptions>(options =>
{
    options.Widths = [480, 960];
    options.Quality = 75;
});
```

| `ImageProcessorOptions` property | Default | Behavior                                                                                                                            |
| -------------------------------- | ------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `IReadOnlyList<int>? Widths`     | `null`  | Widths of the smaller variants. `null` uses 320, 640, 960, and 1280. Widths at or above the source width or `MaxWidth` are skipped. |
| `int MaxWidth`                   | `1920`  | The widest variant. Every image gets one variant at its own width, capped at this value.                                            |
| `int Quality`                    | `80`    | Lossy WebP quality from 1 to 100.                                                                                                   |

- Invalid values make `RunAsync` fail before any output.
- Changing a setting re-encodes images and renders their pages again on the next publish.

### Image processing

To replace the default image processor, implement `IImageProcessor` from `Kiji.Assets` and
register it as a singleton:

```csharp
builder.Services.AddSingleton<IImageProcessor, MyImageProcessor>();
```

| Member                                                                                                                                | Behavior                                                                                      |
| ------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `Task<ProcessedImageInfo> ProcessAsync(string sourceFilePath, string outputDirectory, CancellationToken cancellationToken = default)` | Writes variants of the source image into `outputDirectory` and describes them.                |
| `string? CacheIdentity`                                                                                                               | Identifies the implementation and settings for reuse across publishes. `null` disables reuse. |

`ProcessedImageInfo` has the source's `OriginalWidth` and `OriginalHeight`, used for the
`<img>` size, and `Variants`: `ImageVariant(string FileName, int Width)` entries in
ascending width, with `FileName` relative to `outputDirectory`. All variants go into
`srcset`, and the last is also the `src`.

The processor must support concurrent calls and keep no page or content state. Include
external configuration and encoder versions in `CacheIdentity`; `AlwaysRender` and
`KijiForce` do not clear image caches.

## Page reuse controls

### `AlwaysRenderAttribute`

`[AlwaysRender]` from the `Kiji` namespace makes every page that renders the component,
including through layouts and child components, render on every publish. The attribute is
inherited. See [external data](../incremental-builds/#render-pages-that-use-external-data).

## Generated files

### Feeds

Import `Kiji.Feeds`. This example publishes `feed.xml` from the `BlogPost` model in
[Getting started](../getting-started/#add-a-markdown-page), newest first:

```csharp
builder.AddRssFeed(services => services
    .GetRequiredService<ContentCollection<BlogPost>>()
    .OrderByDescending(post => post.Date)
    .Select(post => new FeedItem(post.Title, post.Description, post.Date, post.RelativePath)));
```

Link the feed among the shared head tags so that feed readers can find it:

```razor
<link rel="alternate" type="application/rss+xml" title="@Site.Name"
      href="@Site.ResolveUrl("feed.xml").AbsoluteUri" />
```

`AddRssFeed(Func<IServiceProvider, IEnumerable<FeedItem>> items, string path = "feed.xml")`
writes an RSS 2.0 feed with items in the given order. The channel's title, description,
language, and URLs come from `SiteInfo`.

`FeedItem(string Title, string Description, DateTimeOffset PublishedAt, string RelativePath)`
describes one item. `RelativePath` is the page path relative to `BaseUrl` without a leading
`/`, such as `posts/hello/`, or `""` for the home page. Times are written in UTC.

### Sitemaps

Import `Kiji.Sitemaps`:

```csharp
builder.AddSitemap(excludedPaths: ["preview/", "internal/status/"]);
```

`AddSitemap(string path = "sitemap.xml", IEnumerable<string>? excludedPaths = null)` lists
every generated page except `404.html`. Each excluded path removes the one page with that
exact path, ignoring case: `preview/` removes `/preview/` but not `/preview/draft/`.

### Custom artifacts

`AddArtifact(string outputRelativePath, Func<Stream, SiteOutputContext, CancellationToken, Task> write)`
writes a file after all pages. Write to the stream without disposing it.

| `SiteOutputContext` property        | Behavior                                       |
| ----------------------------------- | ---------------------------------------------- |
| `SiteInfo Site`                     | The site metadata.                             |
| `IReadOnlyList<SitePageInfo> Pages` | Every generated page.                          |
| `IServiceProvider Services`         | Site services, including content dictionaries. |

`SitePageInfo` has two properties: `RelativePath`, the page URL relative to `BaseUrl` such
as `posts/hello/` (`""` for the home page), and `OutputRelativePath`, the file path in the
output such as `posts/hello/index.html`, using the operating system's directory separator.

## Unsupported Blazor features

Kiji renders static HTML without a Blazor runtime. Event handlers such as `@onclick`
never run in the browser, and `OnAfterRender` and `OnAfterRenderAsync` never run at all.
Use a `<script>` element or a JavaScript module for browser behavior.

### NavigationManager and NavLink

Both render, so Razor class libraries that use them still work. `BaseUri` is
`SiteInfo.BaseUrl` and `Uri` is `PageInfo.Url`, without a query string. `NavLink` renders
its active class but does not rewrite `href`. `NavigateTo` and `Refresh` throw
`NotSupportedException`, and `NotFound()` does not select the 404 page; configure
redirects on your host. In your own components, prefer `PageInfo`, `SiteInfo`, and `<a>`.

### JavaScript interop

`IJSRuntime` can be injected, but its calls throw `InvalidOperationException`.
