---
title: API reference
description: Types and members of the Kiji package, and the MSBuild property it reads.
order: 100
---

`Program.cs` examples assume `using Kiji;`,
`using Microsoft.Extensions.DependencyInjection;`, and a `builder` from
`StaticSite.CreateBuilder(args)`.

- [`Kiji` namespace](#kiji-namespace): the site builder, site and page metadata, content
  collections, and site-wide files
- [`Kiji.Markdown` namespace](#kijimarkdown-namespace): Markdown content
- [`Kiji.Assets` namespace](#kijiassets-namespace): image processing
- [`Kiji.Feeds` namespace](#kijifeeds-namespace): RSS feeds
- [`Kiji.Sitemaps` namespace](#kijisitemaps-namespace): sitemaps
- [Blazor APIs](#blazor-apis): ASP.NET Core components and services in static pages
- [MSBuild properties](#msbuild-properties): `KijiForce`

## `Kiji` namespace

### `StaticSite`

Runs a site configured with `StaticSiteBuilder`:

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

| Member | Behavior |
| --- | --- |
| `static StaticSiteBuilder CreateBuilder(string[] args)` | Creates a builder for the current project. While serving, `--environment` in `args` selects the environment, and `args` go to ASP.NET Core configuration, such as `--urls`. Publishing ignores them. |
| `Task<int> RunAsync(CancellationToken cancellationToken = default)` | Serves during `dotnet watch` or generates the site during `dotnet publish`, then disposes the site. Returns 1 when a publish is canceled and 0 otherwise; exceptions propagate. Return its result from `Program.cs`. |

### `StaticSiteBuilder`

Holds every registration for the site. Registration methods return the builder, so calls
can be chained. After `Build`, `SiteInfo`, `Services`, and the registration methods can no
longer be changed.

| Property | Behavior |
| --- | --- |
| `SiteInfo SiteInfo` | Required site metadata. See [`SiteInfo`](#siteinfo). |
| `IServiceCollection Services` | Services for components and site factories. See [Services](#services). |
| `IHostEnvironment Environment` | The environment name and project directory. See [Environment](#environment). |
| `IConfigurationManager Configuration` | Site configuration. See [Configuration](#configuration). |
| `ILoggingBuilder Logging` | Logging providers and filters. See [Logging](#logging). |

| Method | Behavior |
| --- | --- |
| `UseDefaultLayout<TLayout>()` | Sets the layout for pages without their own `@layout`. Without it, pages render directly inside `<body>`. |
| `AddPageAssembly(Assembly assembly)` | Discovers routed components in another assembly. Kiji always discovers `@page` routes on public, non-abstract components in the entry assembly. Adding an assembly twice has no effect. |
| `AddPageParameters<TPage>(Func<IServiceProvider, IEnumerable<object>> parameters)` | Adds parameter sets for a component. The factory runs once per build or reload and can read content. See [Page parameters](#page-parameters). |
| `UseNotFoundPage<TPage>()` | Writes the component as `404.html` instead of at its route. It needs exactly one route, must be in a page assembly, and cannot also receive page parameters. A later call replaces it. |
| `AddArtifact(string outputRelativePath, Func<Stream, SiteOutputContext, CancellationToken, Task> write)` | Writes a file after all pages. Write to the stream without disposing it. Only `dotnet publish` writes artifacts, including feeds and sitemaps; the development server returns 404 for them. See [`SiteOutputContext`](#siteoutputcontext). |
| `StaticSite Build()` | Validates the services, makes them read-only, and returns the site. Call it once. Content is loaded only when the site runs. |

Extension methods in other namespaces register content and generated files:

| Method | Namespace |
| --- | --- |
| [`AddMarkdownContent`, `AddKeyedMarkdownContent`](#markdownstaticsitebuilderextensions) | `Kiji.Markdown` |
| [`AddRssFeed`](#rssfeedstaticsitebuilderextensions) | `Kiji.Feeds` |
| [`AddSitemap`](#sitemapstaticsitebuilderextensions) | `Kiji.Sitemaps` |

#### Services

`Services` is a standard
[.NET dependency injection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview)
collection:

```csharp
builder.Services.AddScoped<RelatedPosts>();
```

- Register services before `Build`, which reports missing dependencies.
- Options with `ValidateOnStart()` are validated when the site starts, before any output.
- Each page render has its own scope, shared by its page, layout, and child components.
- Content loaders, page parameter and feed factories, and artifact writers cannot resolve
  scoped services.
- During `dotnet watch`, content changes do not recreate singletons.
- Kiji registers `SiteInfo`, `PageInfo`, `IHostEnvironment`, `IConfiguration`,
  `NavigationManager`, `IJSRuntime`, and each `ContentCollection<T>`. These registrations
  cannot be added, removed, or replaced.

#### Environment

`Environment` is a standard
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

#### Configuration

`Configuration` is a standard
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
- The development server answers only requests for `localhost`, `127.0.0.1` and `[::1]`.
  To use another hostname or IP address, add it to `AllowedHosts`, for example in
  `appsettings.Development.json`, and keep the local entries:
  `"AllowedHosts": "localhost;127.0.0.1;[::1];dev.example"`.

#### Logging

`Logging` is a standard
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

#### Page parameters

`AddPageParameters<TPage>` supplies the values for a component's routes. See
[Routing](../routing/) for examples. Parameter sets follow these rules:

- Each set is an anonymous object or a string-keyed dictionary. Names match public
  writable `[Parameter]` properties, ignoring case. Values keep their types; route values
  are formatted with the invariant culture.
- Each set is rendered at every parameterized route of the component, and fixed routes
  beside them render once with default values. A component with only fixed routes renders
  the set at each of its routes instead of its defaults, so it takes at most one set; an
  empty sequence leaves it out.
- Omitted parameters keep their defaults; `EditorRequired` is not checked.
- Unknown names, wrong types, missing or empty route values, and duplicate output paths
  fail before rendering.
- Calls for the same component add up; an empty sequence generates no pages.

A page's HTML can be reused only when every parameter value is `null`, a `string`, `bool`,
`char`, an 8- to 64-bit integer, an enum, a `Guid`, or an item instance obtained from a
`ContentCollection<T>`. One item can go to several pages, for example as one post's `Post`
and the next post's `Previous`. Each page then depends on that item's whole file, even when
the component reads only projected model properties. Any other value makes the page render
on every publish, including anonymous objects, record copies made with a `with`
expression, arrays or lists of items, and an instance that a projection returns for more
than one file.

Resolve items inside the factory each time it runs. While serving, the factory runs again
after content changes, and items kept from an earlier run show stale content.

### `SiteInfo`

Site metadata, assigned to `StaticSiteBuilder.SiteInfo` and injectable into components.

| Property | Default | Behavior |
| --- | --- | --- |
| `Uri BaseUrl` | Required | Absolute HTTP or HTTPS URL without a query or fragment. Its path, with `/` appended if missing, is the site's base path. |
| `string Name` | Required | Site name, also the RSS channel title. Must not be blank. |
| `string Description` | `""` | Site description, also the RSS channel description. |
| `string Language` | `"en"` | Written to `<html lang>` and the RSS channel. |
| `string Author` | `""` | For your components; Kiji does not write it. |

`Uri ResolveUrl(string path)` returns the absolute URL of a site-relative path. A leading
`/` is ignored, so `"about/"` and `"/about/"` both stay under the base path, and `""`
returns `BaseUrl`. External URLs and `..` are rejected.

### `PageInfo`

Describes the page being rendered. Pages, layouts, child components, and scoped services
can inject it; it is unavailable outside page rendering.

| Property | Behavior |
| --- | --- |
| `Uri Url` | The page's absolute public URL, including the base path. While serving, it is still the published URL, not the local request URL. |

```razor
@inject PageInfo Page

<HeadContent>
    <link rel="canonical" href="@Page.Url.AbsoluteUri" />
</HeadContent>
```

### `ContentCollection<T>`

An `IReadOnlyCollection<T>` of content items, resolved through dependency injection and
registered with the [Markdown methods](#markdownstaticsitebuilderextensions). Markdown IDs
are paths relative to the content directory, using `/` and including `.md`; they do not
determine URLs. IDs ignore case. Reads are tracked as
[page dependencies](../incremental-builds/#page-dependencies); values assigned to items
after loading are not.

| Member | Behavior |
| --- | --- |
| `int Count` | Number of items; observes the collection. |
| `GetEnumerator()` | Enumerates items, Markdown files sorted by path ignoring case; observes the collection. |
| `T this[string id]` | Looks up an item; throws `KeyNotFoundException` if absent. |
| `bool TryGetValue(string id, out T? item)` | Looks up an item, or returns `false`. |
| `bool Contains(string id)` | Tests whether the item exists. |

An ID lookup observes that item; a missing ID observes the collection.

### `SiteOutputContext`

Passed to the writer registered with `AddArtifact`.

| Property | Behavior |
| --- | --- |
| `SiteInfo Site` | The site metadata. |
| `IReadOnlyList<SitePageInfo> Pages` | Every generated page. |
| `IServiceProvider Services` | Site services, including content collections. |

### `SitePageInfo`

A generated page, listed in `SiteOutputContext.Pages`.

| Property | Behavior |
| --- | --- |
| `string RelativePath` | The page URL relative to `BaseUrl`, such as `posts/hello/`, or `""` for the home page. |
| `string OutputRelativePath` | The file path in the output, such as `posts/hello/index.html`, using the operating system's directory separator. |

### `AlwaysRenderAttribute`

`[AlwaysRender]` makes every page that renders the component, including through layouts
and child components, render on every publish. The attribute is inherited. See
[external data](../incremental-builds/#render-pages-that-use-external-data).

## `Kiji.Markdown` namespace

See [Markdown and images](../markdown/) for examples.

### `MarkdownStaticSiteBuilderExtensions`

Each method loads the `*.md` files of one source and registers a `ContentCollection<T>`:

| Method | Registers |
| --- | --- |
| `AddMarkdownContent<TFrontMatter>(Action<MarkdownContentOptions>? configure = null)` | `ContentCollection<MarkdownContent<TFrontMatter>>` |
| `AddMarkdownContent<TFrontMatter,TModel>(Func<MarkdownContent<TFrontMatter>,TModel> select, Action<MarkdownContentOptions>? configure = null)` | `ContentCollection<TModel>`, converting each file with `select` |
| `AddKeyedMarkdownContent<TFrontMatter>(string serviceKey, Action<MarkdownContentOptions>? configure = null)` | `ContentCollection<MarkdownContent<TFrontMatter>>` under `serviceKey` |
| `AddKeyedMarkdownContent<TFrontMatter,TModel>(string serviceKey, Func<MarkdownContent<TFrontMatter>,TModel> select, Action<MarkdownContentOptions>? configure = null)` | `ContentCollection<TModel>` under `serviceKey` |

- Resolve a keyed collection with
  `services.GetRequiredKeyedService<ContentCollection<T>>(serviceKey)`.
- Each pair of element type and service key can be registered once, and an unkeyed
  collection can sit beside keyed collections of the same type. Service keys must not be
  empty or whitespace.
- The `configure` callback runs during registration. See
  [`MarkdownContentOptions`](#markdowncontentoptions).
- A projection should return a new instance for each item; see
  [page dependencies](../incremental-builds/#page-dependencies) for what each read depends
  on.

### `MarkdownContentOptions`

Selects the files of one source, through the `configure` callback.

| Property | Behavior |
| --- | --- |
| `string Directory` | Directory to load recursively, absolute or relative to the project. It must exist. Default `"content"`. |
| `Func<FileInfo,bool>? FileFilter` | Returns `false` to skip a file. Default: load every `*.md` file. |

### `MarkdownOptions`

Rendering settings for every source. Configure them with
`builder.Services.Configure<MarkdownOptions>(options => ...)`.

| Property | Behavior |
| --- | --- |
| `MarkdownPipelineBuilder Pipeline` | Markdig builder. Defaults: GFM-style tables, task lists, autolinks and strikethrough; footnotes, GitHub heading IDs and generic attributes. Use `Extensions.Replace` to change a registered extension and `DocumentProcessed` to change the parsed document. |
| `DeserializerBuilder Yaml` | YamlDotNet builder. Defaults: camelCase matched case-insensitively, errors on unknown keys, dates required to start with `yyyy-MM-dd`, and UTC for dates without an offset. |

### `MarkdownContent<TFrontMatter>`

One Markdown file in a collection.

| Member | Behavior |
| --- | --- |
| `string Path` | The path relative to the content directory, using `/`. Also the collection ID. |
| `TFrontMatter FrontMatter` | The parsed front matter. |
| `ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)` | Renders the body to HTML and writes its local images beside the current page. |

Call `RenderAsync` while rendering a page, because local images are written beside that
page.

### `MarkdownBody<TFrontMatter>`

A component that renders a body with no wrapper element. Like `RenderAsync`, it writes
local images beside the current page.

| Parameter | Behavior |
| --- | --- |
| `MarkdownContent<TFrontMatter> Content` | Required. The content to render. |

```razor
<MarkdownBody Content="@content" />
```

## `Kiji.Assets` namespace

### `ImageProcessorOptions`

Settings for the default image processor, configured with the
[options pattern](https://learn.microsoft.com/dotnet/core/extensions/options):

```csharp
builder.Services.Configure<ImageProcessorOptions>(options =>
{
    options.Widths = [480, 960];
    options.Quality = 75;
});
```

| Property | Default | Behavior |
| --- | --- | --- |
| `IReadOnlyList<int>? Widths` | `null` | Widths of the smaller variants. `null` uses 320, 640, 960, and 1280. Widths at or above the source width or `MaxWidth` are skipped. |
| `int MaxWidth` | `1920` | The widest variant. Every image gets one variant at its own width, capped at this value. |
| `int Quality` | `80` | Lossy WebP quality from 1 to 100. |

- Invalid values make `RunAsync` fail before any output.
- Changing a setting re-encodes images and renders their pages again on the next publish.
- A replacement `IImageProcessor` does not read these settings.

### `IImageProcessor`

Writes the variants of a local image. Kiji registers a default WebP processor. To replace
it, implement the interface and register it as a singleton:

```csharp
builder.Services.AddSingleton<IImageProcessor, MyImageProcessor>();
```

| Member | Behavior |
| --- | --- |
| `Task<ProcessedImageInfo> ProcessAsync(string sourceFilePath, string outputDirectory, CancellationToken cancellationToken = default)` | Writes variants of the source image into `outputDirectory` and describes them. |
| `string? CacheIdentity` | Identifies the implementation and settings for reuse across publishes. `null`, the default, disables reuse. |

- The last unkeyed registration must be a singleton, or `Build` fails.
- The processor must support concurrent calls and keep no page or content state.
- Include external configuration and encoder versions in `CacheIdentity`. `AlwaysRender`
  and `KijiForce` do not clear image caches.

### `ProcessedImageInfo`

Returned by `IImageProcessor.ProcessAsync`.

| Property | Behavior |
| --- | --- |
| `int OriginalWidth` | Source width in pixels, used for the `<img>` size. |
| `int OriginalHeight` | Source height in pixels, used for the `<img>` size. |
| `IReadOnlyList<ImageVariant> Variants` | The variants in ascending width. All go into `srcset`, and the last is also the `src`. |

### `ImageVariant`

`ImageVariant(string FileName, int Width)` describes one variant. `FileName` is relative
to the `outputDirectory` passed to `ProcessAsync`, and `Width` is in pixels.

## `Kiji.Feeds` namespace

### `RssFeedStaticSiteBuilderExtensions`

`AddRssFeed(Func<IServiceProvider, IEnumerable<FeedItem>> items, string path = "feed.xml")`
writes an RSS 2.0 feed with items in the given order. The channel's title, description,
language, and URLs come from `SiteInfo`.

This example publishes `feed.xml` from the `BlogPost` model in
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

### `FeedItem`

`FeedItem(string Title, string Description, DateTimeOffset PublishedAt, string RelativePath)`
describes one item. `RelativePath` is the page path relative to `BaseUrl` without a leading
`/`, such as `posts/hello/`, or `""` for the home page. Times are written in UTC.

## `Kiji.Sitemaps` namespace

### `SitemapStaticSiteBuilderExtensions`

`AddSitemap(string path = "sitemap.xml", IEnumerable<string>? excludedPaths = null)` lists
every generated page except `404.html`. Each excluded path removes the one page with that
exact path, ignoring case: `preview/` removes `/preview/` but not `/preview/draft/`.

```csharp
builder.AddSitemap(excludedPaths: ["preview/", "internal/status/"]);
```

## Blazor APIs

Kiji renders components to static HTML without a Blazor runtime in the browser.

### `PageTitle` and `HeadContent`

Use Blazor's `PageTitle` and `HeadContent`; Kiji's `HeadOutlet` writes them into `<head>`
after asynchronous rendering finishes. Separate `HeadContent` blocks replace each other.
See [Layouts and the document head](../concepts/#layouts-and-the-document-head).

### `ComponentBase.Assets`

`Assets["css/app.css"]` returns the asset's public URL with the base path and, when
available, a fingerprint. Unknown paths are returned unchanged. URLs written directly in
CSS, JavaScript, or HTML are not rewritten.

### `ImportMap`

Place Blazor's `<ImportMap />` in `HeadContent` before scripts that use JavaScript imports.
Its `ImportMapDefinition` parameter accepts a custom map. See the
[ImportMap component](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0#importmap-component).

### `NavigationManager` and `NavLink`

Both render, so Razor class libraries that use them still work. `BaseUri` is
`SiteInfo.BaseUrl` and `Uri` is `PageInfo.Url`, without a query string. `NavLink` renders
its active class but does not rewrite `href`. `NavigateTo` and `Refresh` throw
`NotSupportedException`, and `NotFound()` does not select the 404 page; configure
redirects on your host. In your own components, prefer `PageInfo`, `SiteInfo`, and `<a>`.

### `IJSRuntime`

`IJSRuntime` can be injected, but its calls throw `InvalidOperationException`.

### Event handlers and after-render methods

Event handlers such as `@onclick` never run in the browser, and `OnAfterRender` and
`OnAfterRenderAsync` never run at all. Use a `<script>` element or a JavaScript module for
browser behavior.

## MSBuild properties

| Property | Behavior |
| --- | --- |
| `KijiForce` | `true` renders every page in that publish, as in `dotnet publish -p:KijiForce=true`. Later publishes reuse the results. It does not clear image caches. See [Reset and inspect](../incremental-builds/#reset-and-inspect). |
