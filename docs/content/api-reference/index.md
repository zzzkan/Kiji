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

| Member                                                                         | Behavior                                                                                                                                            |
| ------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `static StaticSiteBuilder StaticSite.CreateBuilder(string[] args)`             | Creates a builder for the current project. During development, `args` go to ASP.NET Core configuration, for example `--urls`.                       |
| `SiteInfo StaticSiteBuilder.SiteInfo`                                          | Required site metadata.                                                                                                                             |
| `IServiceCollection StaticSiteBuilder.Services`                                | Services for components and site factories. See [Services](#services).                                                                              |
| `IHostEnvironment StaticSiteBuilder.Environment`                               | The host environment. See [Environment](#environment).                                                                                              |
| `ConfigurationManager StaticSiteBuilder.Configuration`                         | Site configuration. See [Configuration](#configuration).                                                                                            |
| `ILoggingBuilder StaticSiteBuilder.Logging`                                    | Logging providers and filters. See [Logging](#logging).                                                                                             |
| `StaticSite StaticSiteBuilder.Build()`                                         | Validates the services and makes the builder and `Services` read-only. Call it once. Content is not loaded yet.                                     |
| `Task<int> StaticSite.RunAsync(CancellationToken cancellationToken = default)` | Serves during `dotnet watch` and generates the site during `dotnet publish`; return its exit code. A site runs only once and is disposed afterward. |

### Environment

`builder.Environment` is a standard
[`IHostEnvironment`](https://learn.microsoft.com/dotnet/core/extensions/generic-host#ihostenvironment),
also injectable into components and services. `IsDevelopment()` and similar checks need
`using Microsoft.Extensions.Hosting;`:

```csharp
if (builder.Environment.IsDevelopment())
{
    // For example, also list draft posts while serving.
}
```

- `EnvironmentName` comes from `DOTNET_ENVIRONMENT`. Without it, the name is `Production`
  during `dotnet publish` and `Development` while serving. It is read-only, because it
  selects the configuration files when the builder is created; `ApplicationName` is the
  entry assembly name.
- `ContentRootPath` is the project directory and cannot be changed.
- Changing the environment name renders every page again on the next publish.

### Configuration

`builder.Configuration` is a standard
[configuration](https://learn.microsoft.com/dotnet/core/extensions/configuration), also
injectable into components and services as `IConfiguration` from
`Microsoft.Extensions.Configuration`. It reads these sources from the project directory,
later ones overriding earlier ones:

1. `appsettings.json`
2. `appsettings.{Environment}.json`, for example `appsettings.Production.json` during `dotnet publish`
3. [User secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), in the
   `Development` environment only

Bind a section to options, or read values in a component:

```csharp
builder.Services.Configure<ImageProcessorOptions>(builder.Configuration.GetSection("Images"));
```

```razor
@inject IConfiguration Configuration

<p>@Configuration["Site:Tagline"]</p>
```

- Environment variables and command-line arguments are not read, because they differ
  between machines and `dotnet publish` passes no arguments. Add a source explicitly, with
  a prefix: `builder.Configuration.AddEnvironmentVariables("MYSITE_")`.
- Every configuration key and value, including keys set to `null`, is part of the publish
  fingerprint: changing any of them renders every page again. The cache stores only a SHA-256 hash of the values.
- Files are read once. Restart `dotnet watch` after editing them.

### Logging

`builder.Logging` is a standard
[`ILoggingBuilder`](https://learn.microsoft.com/dotnet/core/extensions/logging). By default,
Kiji writes `Warning` and higher to the console, using standard error so that
`dotnet publish` shows it. This includes exceptions caught by
[`ErrorBoundary`](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/handle-errors?view=aspnetcore-10.0#error-boundaries).
The `Logging` configuration section applies as in other .NET hosts. `LogLevel` and the
extension methods need `using Microsoft.Extensions.Logging;`:

```csharp
builder.Logging.SetMinimumLevel(LogLevel.Information);
```

Information messages go to standard output, which `dotnet publish` hides at its default
verbosity. Removing the console provider with `ClearProviders()` also hides Kiji's warnings.

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

- Register services before `Build`; the collection is read-only afterward. `Build` reports
  missing dependencies.
- Options registered with `ValidateOnStart()` are validated when the site starts running,
  before any output, as in other .NET hosts. `RunAsync` throws `OptionsValidationException`
  for invalid values.
- Each page render has its own scope, shared by its page, layout, and child components.
- Content loaders, page parameter and feed factories, and artifact writers cannot resolve
  scoped services.
- `SiteInfo`, `PageInfo`, `IHostEnvironment`, `IConfiguration`, and `ContentDictionary<T>` come
  from Kiji and cannot be replaced.
- Components and services can inject `ILogger<T>`. See [Logging](#logging).

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

### Content sources and ContentDictionary

`ContentDictionary<T>` is a read-only `IReadOnlyDictionary<string, T>` that components and
factories inject. Keys ignore case and do not determine URLs; Markdown keys are paths
relative to the content directory, using `/`. Enumeration keeps source order. Reads are
tracked as [page dependencies](../incremental-builds/#page-dependencies).

`AddContentSource` registers custom data, such as JSON, or an index derived from other
content, such as tag counts. The loader runs when the content is first read, and again
after content changes during development. Treat loaded values as immutable.

#### `AddContentSource<T>(Func<IServiceProvider, IReadOnlyList<T>> loader)`

Items get the keys `"0"`, `"1"`, and so on. Pages that read them render on every publish.
This example counts the tags of the [Markdown example](../markdown/#register-markdown-content):

```csharp
public sealed record TagSummary(string Name, int Count);
```

```csharp
builder.AddMarkdownContent<FrontMatter>();
builder.AddContentSource<TagSummary>(LoadTags);

static IReadOnlyList<TagSummary> LoadTags(IServiceProvider services) =>
    services.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
        .Values
        .SelectMany(post => post.FrontMatter.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
        .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => new TagSummary(group.Key, group.Count()))
        .ToArray();
```

#### `AddContentSource<T>(string sourceId, Func<IServiceProvider, IReadOnlyList<ContentEntry<T>>> loader)`

Give each item an `Id` and a `Digest` so that pages reading unchanged items can be reused:

```csharp
builder.AddContentSource<TagSummary>("tag-summary", services =>
    LoadTags(services)
        .Select(tag => new ContentEntry<TagSummary>(Id: tag.Name, Value: tag, Digest: tag.ToString()))
        .ToArray());
```

Keep `sourceId` stable between builds. `Id` identifies an item regardless of its position.
`Digest` must change whenever anything affecting the value changes; here the record's
`ToString()` includes both fields. A wrong digest can reuse stale HTML, so use `null` when
no reliable digest exists; pages that read the item or its collection then always render.

### Markdown

Import `Kiji.Markdown`. See [Markdown and images](../markdown/) for examples.

| Method                                                                                                                                  | Behavior                                                                             |
| --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `AddMarkdownContent<TFrontMatter>(Action<MarkdownOptions>? configure = null)`                                                           | Loads `*.md` files and registers `ContentDictionary<MarkdownContent<TFrontMatter>>`. |
| `AddMarkdownContent<TFrontMatter,TModel>(Func<MarkdownContent<TFrontMatter>,TModel> select, Action<MarkdownOptions>? configure = null)` | Converts each file with `select` and registers `ContentDictionary<TModel>`.          |

| `MarkdownOptions` member                             | Behavior                                                                     |
| ---------------------------------------------------- | ---------------------------------------------------------------------------- |
| `string Directory`                                   | Directory to load, absolute or relative to the project. Default `"content"`. |
| `Func<FileInfo,bool>? FileFilter`                    | Returns `false` to skip a file. Default: load every `*.md` file.             |
| `ConfigureMarkdown(Action<MarkdownPipelineBuilder>)` | Adds Markdig configuration after Kiji's defaults.                            |
| `ConfigureYaml(Action<DeserializerBuilder>)`         | Adds YamlDotNet configuration after Kiji's defaults.                         |
| `AddHtmlPostProcessor(Func<string,string>)`          | Transforms the rendered HTML. Processors run in registration order.          |

| `MarkdownContent<TFrontMatter>` member                                         | Behavior                                                                      |
| ------------------------------------------------------------------------------ | ----------------------------------------------------------------------------- |
| `FileInfo FileInfo`                                                            | The source Markdown file.                                                     |
| `TFrontMatter FrontMatter`                                                     | The parsed front matter.                                                      |
| `ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)` | Renders the body to HTML and writes its local images beside the current page. |

Call `RenderAsync` while rendering a page, because local images are written beside that
page.

### Image settings

Configure `ImageProcessorOptions` from `Kiji.Assets` to change the default processor's
output with the standard
[options pattern](https://learn.microsoft.com/dotnet/core/extensions/options):

```csharp
builder.Services.Configure<ImageProcessorOptions>(options =>
{
    options.Widths = [480, 960];
    options.Quality = 75;
});
```

| `ImageProcessorOptions` property | Default | Behavior                                                                                                                                             |
| -------------------------------- | ------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IReadOnlyList<int>? Widths`     | `null`  | Widths of the smaller variants. `null` uses `DefaultWidths`: 320, 640, 960, and 1280. Widths at or above the source width or `MaxWidth` are skipped. |
| `int MaxWidth`                   | `1920`  | The widest variant. Every image gets one variant at its own width, capped at this value.                                                             |
| `int Quality`                    | `80`    | Lossy WebP quality from 1 to 100.                                                                                                                    |

- The settings are validated when the site starts running, before any output: `RunAsync`
  throws `OptionsValidationException` for invalid values.
- Changing a setting re-encodes images and renders their pages again on the next publish.
- The settings apply only to the default processor, not to a replacement `IImageProcessor`.

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
    .GetRequiredService<ContentDictionary<BlogPost>>()
    .Values
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
