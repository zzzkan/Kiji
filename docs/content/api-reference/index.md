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
| `static StaticSiteBuilder CreateBuilder(string[] args)` | Creates a builder for the current project. While serving, `args` can set the environment and development server options such as `--urls`. |
| `Task<int> RunAsync(CancellationToken cancellationToken = default)` | Serves the site during `dotnet watch` and generates it during `dotnet publish`. Return its result from `Program.cs`. |

### `StaticSiteBuilder`

Holds every registration for the site. Configure it before calling `Build`. Registration
methods return the builder, so calls can be chained.

| Property | Behavior |
| --- | --- |
| `SiteInfo SiteInfo` | Required site metadata. See [`SiteInfo`](#siteinfo). |
| `IServiceCollection Services` | Services for components. See [Services](#services). |
| `IHostEnvironment Environment` | The environment name. See [Environment](#environment). |
| `IConfigurationManager Configuration` | Site configuration. See [Configuration](#configuration). |
| `ILoggingBuilder Logging` | Logging providers and filters. See [Logging](#logging). |

| Method | Behavior |
| --- | --- |
| `UseDefaultLayout<TLayout>()` | Sets the layout for pages without their own `@layout`. Without it, pages render directly inside `<body>`. |
| `AddPageAssembly(Assembly assembly)` | Adds pages from another assembly, such as a Razor class library. Pages in the site project are found automatically. |
| `AddPageParameters<TPage>(Func<IServiceProvider, IEnumerable<object>> parameters)` | Supplies parameter values for a component's pages. See [Page parameters](#page-parameters). |
| `UseNotFoundPage<TPage>()` | Writes the component as `404.html` instead of at its route. The component needs exactly one route and cannot also receive page parameters. |
| `AddArtifact(string outputRelativePath, Func<Stream, SiteOutputContext, CancellationToken, Task> write)` | Writes a file after all pages. Write to the stream without disposing it. See [`SiteOutputContext`](#siteoutputcontext). |
| `StaticSite Build()` | Validates the services and returns the site. Call it once. |

Extension methods in other namespaces register content and generated files:

| Method | Namespace |
| --- | --- |
| [`AddMarkdownContent`, `AddKeyedMarkdownContent`](#markdownstaticsitebuilderextensions) | `Kiji.Markdown` |
| [`AddRssFeed`](#rssfeedstaticsitebuilderextensions) | `Kiji.Feeds` |
| [`AddSitemap`](#sitemapstaticsitebuilderextensions) | `Kiji.Sitemaps` |

Only `dotnet publish` writes artifacts, feeds, and sitemaps; the development server
returns 404 for them.

#### Services

`Services` is a standard
[.NET dependency injection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview)
collection:

```csharp
builder.Services.AddScoped<RelatedPosts>();
```

- Each page render has its own scope, shared by its page, layout, and child components.
- Page parameter and feed factories and artifact writers cannot resolve scoped services.
- Kiji's own services, such as `SiteInfo`, `PageInfo`, and `ContentCollection<T>`, cannot
  be replaced.

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

`dotnet publish` uses `DOTNET_ENVIRONMENT` and defaults to `Production`. `dotnet watch`
defaults to `Development`; `--environment`, `DOTNET_ENVIRONMENT`, or
`ASPNETCORE_ENVIRONMENT` changes it.

#### Configuration

`Configuration` is a standard
[configuration](https://learn.microsoft.com/dotnet/core/extensions/configuration), also
injectable as `IConfiguration`. It reads `appsettings.json` and then
`appsettings.{Environment}.json` from the project directory:

```csharp
builder.Services.Configure<ImageProcessorOptions>(builder.Configuration.GetSection("Images"));
```

- Environment variables, command-line arguments, and user secrets are not read. Add a
  source when you need one, such as `builder.Configuration.AddEnvironmentVariables("MYSITE_")`.
- Restart `dotnet watch` after editing the files.
- The development server answers only `localhost`, `127.0.0.1`, and `[::1]`. To use
  another hostname, add it to `AllowedHosts` and keep the local entries, for example
  `"AllowedHosts": "localhost;127.0.0.1;[::1];dev.example"` in `appsettings.Development.json`.

#### Logging

`Logging` is a standard
[`ILoggingBuilder`](https://learn.microsoft.com/dotnet/core/extensions/logging), configured
by the `Logging` section as in other .NET hosts. The console shows warnings and errors,
including exceptions caught by an
[`ErrorBoundary`](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/handle-errors?view=aspnetcore-10.0#error-boundaries),
and Kiji's summary. `Debug` lists each generated file:

```json
{ "Logging": { "LogLevel": { "Kiji.Build": "Debug" } } }
```

- Logs never fail a publish. To stop a publish, throw an exception that no error boundary
  catches.
- In a terminal, `dotnet publish` shows Kiji's messages only with `-v d`, which also shows
  why a publish failed. CI logs always show them.
- Pages reused from an earlier publish do not log their messages again.

#### Page parameters

`AddPageParameters<TPage>` supplies the values for a component's routes. See
[Routing](../routing/) for examples.

- Each set is an anonymous object or a dictionary with string keys. Names match
  `[Parameter]` properties, ignoring case, and values must already have the property's type.
- Each set is rendered at every parameterized route of the component. A component with only
  fixed routes takes at most one set, which replaces its default values.
- Omitted parameters keep their defaults; `EditorRequired` is not checked.
- Unknown names, wrong types, missing route values, and duplicate output paths fail before
  rendering.
- Calls for the same component add up; an empty sequence generates no pages.

A page's HTML is reused across publishes only when every value is `null`, a `string`,
`bool`, `char`, an integer, an enum, a `Guid`, or an item from a `ContentCollection<T>`.
The same item can go to several pages, for example as `Post` on one page and `Previous`
on the next. Other values, such as arrays or objects created in the factory, make the
page render on every publish.

### `SiteInfo`

Site metadata, assigned to `StaticSiteBuilder.SiteInfo` and injectable into components.

| Property | Default | Behavior |
| --- | --- | --- |
| `Uri BaseUrl` | Required | The site's public URL, such as `https://example.com/blog/`. Its path is the base path of every page. |
| `string Name` | Required | Site name, also the RSS channel title. |
| `string Description` | `""` | Site description, also the RSS channel description. |
| `string Language` | `"en"` | Written to `<html lang>` and the RSS channel. |
| `string Author` | `""` | For your components; Kiji does not write it. |

`Uri ResolveUrl(string path)` returns the absolute URL of a site-relative path. A leading
`/` is ignored, so `"about/"` and `"/about/"` both stay under the base path, and `""`
returns `BaseUrl`.

### `PageInfo`

Describes the page being rendered. Inject it in pages, layouts, and components.

| Property | Behavior |
| --- | --- |
| `Uri Url` | The page's absolute public URL, including the base path. The development server uses the published URL too. |

```razor
@inject PageInfo Page

<HeadContent>
    <link rel="canonical" href="@Page.Url.AbsoluteUri" />
</HeadContent>
```

### `ContentCollection<T>`

The items of one content source, injected as a service. Markdown IDs are paths relative to
the content directory, such as `posts/hello/index.md`, and ignore case. For how reads affect
page reuse, see [what changes cause work](../incremental-builds/#what-changes-cause-work).

| Member | Behavior |
| --- | --- |
| `int Count` | Number of items. |
| `GetEnumerator()` | Enumerates items in path order. |
| `T this[string id]` | Looks up an item; throws `KeyNotFoundException` if absent. |
| `bool TryGetValue(string id, out T? item)` | Looks up an item, or returns `false`. |
| `bool Contains(string id)` | Tests whether the item exists. |

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
| `string OutputRelativePath` | The output file, such as `posts/hello/index.html`, with `\` separators on Windows. |

### `AlwaysRenderAttribute`

`[AlwaysRender]` renders every page that contains the component, including through a
layout, on every publish. Derived components inherit it. See
[external data](../incremental-builds/#render-pages-that-use-external-data).

## `Kiji.Markdown` namespace

See [Markdown and images](../markdown/) for examples.

### `MarkdownStaticSiteBuilderExtensions`

Each method loads the `*.md` files of one directory and registers a `ContentCollection<T>`:

| Method | Registers |
| --- | --- |
| `AddMarkdownContent<TFrontMatter>(Action<MarkdownContentOptions>? configure = null)` | `ContentCollection<MarkdownContent<TFrontMatter>>` |
| `AddMarkdownContent<TFrontMatter,TModel>(Func<MarkdownContent<TFrontMatter>,TModel> select, Action<MarkdownContentOptions>? configure = null)` | `ContentCollection<TModel>`, converting each file with `select` |
| `AddKeyedMarkdownContent<TFrontMatter>(string serviceKey, Action<MarkdownContentOptions>? configure = null)` | `ContentCollection<MarkdownContent<TFrontMatter>>` under `serviceKey` |
| `AddKeyedMarkdownContent<TFrontMatter,TModel>(string serviceKey, Func<MarkdownContent<TFrontMatter>,TModel> select, Action<MarkdownContentOptions>? configure = null)` | `ContentCollection<TModel>` under `serviceKey` |

Resolve a keyed collection with
`services.GetRequiredKeyedService<ContentCollection<T>>(serviceKey)`. Each element type can
be registered once per key.

### `MarkdownContentOptions`

Selects the files to load, through the `configure` callback.

| Property | Behavior |
| --- | --- |
| `string Directory` | Directory to load recursively, absolute or relative to the project. It must exist. Default `"content"`. |
| `Func<FileInfo,bool>? FileFilter` | Returns `false` to skip a file. Default: load every `*.md` file. |

### `MarkdownOptions`

Parsing settings for all Markdown content. Configure them with
`builder.Services.Configure<MarkdownOptions>(options => ...)`.

| Property | Behavior |
| --- | --- |
| `MarkdownPipelineBuilder Pipeline` | Markdig builder. Defaults: GFM-style tables, task lists, autolinks and strikethrough; footnotes, GitHub heading IDs and generic attributes. |
| `DeserializerBuilder Yaml` | YamlDotNet builder for front matter. Defaults: camelCase keys matched case-insensitively, errors on unknown keys, and UTC for dates without an offset. |

### `MarkdownContent<TFrontMatter>`

One Markdown file in a collection.

| Member | Behavior |
| --- | --- |
| `string Path` | The path relative to the content directory, using `/`. Also the collection ID. |
| `TFrontMatter FrontMatter` | The parsed front matter. |
| `ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)` | Renders the body to HTML. Call it while rendering a page, because local images are written beside that page. |

### `MarkdownBody<TFrontMatter>`

A component that renders a Markdown body with no wrapper element.

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
| `int MaxWidth` | `1920` | The widest variant. Every image also gets a variant at its own width, up to this value. |
| `int Quality` | `80` | Lossy WebP quality from 1 to 100. |

A custom `IImageProcessor` ignores these settings.

### `IImageProcessor`

Writes the variants of a local image. To replace the default WebP processor, implement the
interface and register it as a singleton:

```csharp
builder.Services.AddSingleton<IImageProcessor, MyImageProcessor>();
```

| Member | Behavior |
| --- | --- |
| `Task<ProcessedImageInfo> ProcessAsync(string sourceFilePath, string outputDirectory, CancellationToken cancellationToken = default)` | Writes variants of the source image into `outputDirectory` and describes them. |
| `string? CacheIdentity` | Lets later publishes reuse generated images while it stays the same. `null`, the default, disables reuse. |

The processor must support concurrent calls and keep no page or content state. Change
`CacheIdentity` whenever the output would change, including settings and encoder versions;
`KijiForce` does not clear image caches.

### `ProcessedImageInfo`

Returned by `IImageProcessor.ProcessAsync`.

| Property | Behavior |
| --- | --- |
| `int OriginalWidth` | Source width in pixels as displayed, after EXIF orientation. Used for the `<img>` size. |
| `int OriginalHeight` | Source height in pixels as displayed, after EXIF orientation. Used for the `<img>` size. |
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
`/`, such as `posts/hello/`, or `""` for the home page.

## `Kiji.Sitemaps` namespace

### `SitemapStaticSiteBuilderExtensions`

`AddSitemap(string path = "sitemap.xml", IEnumerable<string>? excludedPaths = null)` lists
every generated page except `404.html`. Each excluded path removes only the page with that
exact path: `preview/` removes `/preview/` but not `/preview/draft/`.

```csharp
builder.AddSitemap(excludedPaths: ["preview/", "internal/status/"]);
```

## Blazor APIs

Kiji renders components to static HTML without a Blazor runtime in the browser.

### `PageTitle` and `HeadContent`

Use Blazor's `PageTitle` and `HeadContent` to fill `<head>`. A second `HeadContent`
replaces the first instead of adding to it. See
[Layouts and the document head](../concepts/#layouts-and-the-document-head).

### `ComponentBase.Assets`

`Assets["css/app.css"]` returns the asset's public URL with the base path and, when
available, a fingerprint. Unknown paths are returned unchanged. URLs written directly in
CSS, JavaScript, or HTML are not rewritten.

### `ImportMap`

Place Blazor's `<ImportMap />` in `HeadContent` before scripts that use JavaScript imports.
See the
[ImportMap component](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0#importmap-component).

### `NavigationManager` and `NavLink`

Both render, so Razor class libraries that use them still work. `NavigationManager.Uri` is
the page's public URL, and `NavLink` marks the current page as active. `NavigateTo` and
`Refresh` throw; configure redirects on your host. In your own components, prefer
`PageInfo`, `SiteInfo`, and `<a>`.

### `IJSRuntime`

`IJSRuntime` can be injected, but its calls throw `InvalidOperationException`.

### Event handlers and after-render methods

Event handlers such as `@onclick` never run in the browser, and `OnAfterRender` and
`OnAfterRenderAsync` never run at all. Use a `<script>` element or a JavaScript module for
browser behavior.

## MSBuild properties

| Property | Behavior |
| --- | --- |
| `KijiForce` | `true` renders every page in that publish, as in `dotnet publish -p:KijiForce=true`. Image caches are kept. See [Reset and inspect](../incremental-builds/#reset-and-inspect). |
