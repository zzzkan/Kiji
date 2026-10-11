---
title: API reference
description: Configure pages, content, images, and generated files.
order: 100
---

Configure the site before `RunAsync`. All `StaticSite` registration methods return
the site and may be chained.

- [Site setup](#site-setup)
- [Pages and rendering](#pages-and-rendering)
- [Content and images](#content-and-images)
- [Build inputs and controls](#build-inputs-and-controls)
- [Generated files](#generated-files)

## Site setup

### StaticSite lifecycle

```csharp
var app = StaticSite.Create(args);
app.Info = new SiteInfo
{
    BaseUrl = new Uri("https://example.com/"),
    Name = "My Site",
};

// Add* and Use* registrations

return await app.RunAsync();
```

#### `StaticSite.Create(string[] args)`

Creates a site rooted at its project directory. `args` are forwarded to ASP.NET Core
configuration during development, for example `--urls`. Build the project with Kiji's
targets before running the executable.

#### `StaticSite.Info`

Required site metadata. Assign it before execution.

#### `StaticSite.RunAsync(CancellationToken cancellationToken = default)`

Serves during `dotnet watch` and generates the site during `dotnet publish`.
Await it and return its exit code. A site can run once; configuration becomes read-only
when execution starts, and Kiji disposes its resources when the run ends.

### SiteInfo

`SiteInfo` contains site-wide metadata used by rendered pages, feeds, and sitemaps.

| Property             | Required/default | Behavior                                                                                                                                        |
| -------------------- | ---------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| `Uri BaseUrl`        | Required         | Must be an absolute HTTP or HTTPS URL without a query or fragment. It is normalized to end in `/`; its path is the site's deployment base path. |
| `string Name`        | Required         | Must not be null, empty, or whitespace. Used as site metadata and by RSS.                                                                       |
| `string Description` | `""`             | Site description and default RSS channel description.                                                                                           |
| `string Language`    | `"en"`           | Written to `<html lang>` and the RSS channel language.                                                                                          |
| `string Author`      | `""`             | Author metadata available to the site.                                                                                                          |

#### `SiteInfo.ResolveUrl(string path)`

Combines a site-relative path with `BaseUrl` and returns an absolute `Uri`.
A single leading `/` is ignored: both `Site.ResolveUrl("about/")` and
`Site.ResolveUrl("/about/")` preserve a deployment base path such as `/kiji/`.
An empty string or `"/"` returns `BaseUrl`.

This differs from standard `System.Uri` resolution: `/about/` stays under the site's base
path, and external URLs and parent-directory traversal are not accepted.

## Pages and rendering

### Pages

#### `AddStaticPages()`

Finds public, non-abstract components with fixed routes in the entry assembly. Components
with parameterized routes are ignored. If there is no entry assembly, use the assembly
overload.

#### `AddStaticPages(Assembly assembly)`

Finds fixed routed components in the supplied non-null assembly. Registering the same
assembly repeatedly has no additional effect.

#### `AddPages<TPage>(Func<IServiceProvider, IEnumerable<object>> parameters)`

Registers parameter sets for an `IComponent` with exactly one parameterized route. The
factory is deferred until paths and services are ready and runs once per registration per
site snapshot. Its service provider can resolve registered content dictionaries.

Each returned object is the component's complete parameter set. Anonymous objects and
string/object dictionaries are supported. Names match public writable `[Parameter]`
properties case-insensitively; route parameter names also build the URL. Values retain
their .NET types, although route values are formatted invariantly for their URL segment.
Missing, empty, unknown, or incompatible values fail planning.

Multiple registrations for the same component are concatenated and an empty sequence is
allowed. Duplicate output paths fail. Treat supplied objects as read-only for the snapshot;
Kiji does not clone or dispose them. See [Routing](../routing/) for examples and supported
route templates.

HTML can be reused when parameter values are `null` or have one of these types:
`string`, `bool`, `char`, `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`,
`ulong`, enums, and `Guid`. Other types can be valid component parameters but disable
HTML reuse for that page. Prefer passing a content key and looking up the item during
rendering. See [Incremental builds](../incremental-builds/#pass-stable-page-parameters).

#### `UseNotFoundPage<TComponent>()`

Writes a routed `IComponent` as `/404.html`. The component must declare exactly one route
and cannot also be registered through `AddPages`.

### Layout

#### `UseDefaultLayout<TLayout>()`

Sets the `LayoutComponentBase` used by pages without their own `@layout`. A page's layout
takes precedence, and layouts may nest through their own `@layout`. Without a default,
the page renders directly inside the generated document body.

### Page service

#### `AddPageService<T>()` / `AddPageService<TService, TImplementation>()`

Registers one instance of a concrete class for each page render. Its public constructor
dependencies are resolved automatically; the page, layout, and child components share the
instance. Kiji disposes it when that render finishes.

Page services are unavailable to content loaders, route/feed factories, and artifact
writers.

### Page metadata

#### PageInfo

Inject `Kiji.PageInfo` during rendering. Its read-only `Uri Url` is the page's public
URL, including the deployment base path.

```razor
@inject PageInfo Page
@inject SiteInfo Site

<StaticHeadContent>
    <link rel="canonical" href="@Page.Url.AbsoluteUri" />
</StaticHeadContent>
<a href="@Site.ResolveUrl("about/").AbsolutePath">About</a>
```

Blazor's `NavigationManager` is unsupported and not registered. Use `PageInfo.Url` and
`SiteInfo.ResolveUrl` for links; configure redirects in the hosting platform.

#### StaticHeadContent

`Kiji.Components.StaticHeadContent` is a component that places its `RenderFragment? ChildContent`
inside the generated document `<head>`:

```razor
<StaticHeadContent>
    <meta charset="utf-8" />
    <title>@Title</title>
</StaticHeadContent>
```

All instances append to the head in registration order, with no markup at their position
in the body. Updating or removing one affects only its contribution. Tags are not deduplicated.

Blazor's `Microsoft.AspNetCore.Components.Web.PageTitle`, `HeadContent`, and `HeadOutlet`
are unsupported and throw an actionable error during rendering (publish and development).
Use `StaticHeadContent` with a plain `<title>` element.

### Static assets

The inherited `ComponentBase.Assets` property resolves paths such as `Assets["css/app.css"]`
to public URLs, including fingerprints when available and `SiteInfo.BaseUrl`'s path prefix.
An unknown path is returned unchanged. Literal URLs in CSS, JavaScript, or HTML are not
automatically rewritten. Use `SiteInfo.ResolveUrl` for links to pages and generated files.

Blazor's `ImportMap` component supplies an import map for the site's static assets.
Place `<ImportMap />` inside `StaticHeadContent` before scripts that use JavaScript imports.
Its `ImportMapDefinition` parameter accepts a custom map. See the
[Blazor import map reference](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0#importmap-component).

See [Concepts](../concepts/#static-assets) for asset links and component styles,
[Incremental builds](../incremental-builds/#what-changes-cause-work) for rebuild behavior,
and [Deployment](../deployment/#compression) for compression settings.

## Content and images

### Content sources and ContentDictionary

#### `UseContentSource<T>(Func<IServiceProvider, IReadOnlyList<T>> loader)`

Registers a content source for reference type `T`. The loader runs lazily once for each
site snapshot, after execution paths are known. Its items receive opaque string keys based
on their zero-based order and are exposed as `ContentDictionary<T>`. A given element type
can identify only one registered dictionary. This form has no data digest, so pages
reading its items are rendered on every publish.

#### `UseContentSource<T>(string sourceId, Func<IServiceProvider, IReadOnlyList<ContentEntry<T>>> loader)`

Registers a source with stable, source-local identities. Return
`new ContentEntry<T>(id, value, digest)` for each item. The digest must change whenever
any data affecting the value changes, including data obtained from HTTP or other sources.
Use `null` when that guarantee cannot be made; readers of that item or its collection
lose HTML reuse.
Values are immutable for one build snapshot. Collection enumeration tracks item order,
membership, and all item digests. A missing lookup tracks the collection too.

For either overload, declare file or directory inputs with `AddBuildInput(path)` so
changes refresh the content during development.

#### `ContentDictionary<T>`

Inject this `IReadOnlyDictionary<string, T>` to access `Count`, `Keys`, `Values`, the
indexer, `ContainsKey`, `TryGetValue`, and enumeration. Keys are case-insensitive;
enumeration preserves source order. Markdown keys are source-relative paths using `/`;
explicit custom entries use their supplied IDs. Keys do not determine public URLs.

A successful lookup tracks one item. Enumeration, `Count`, `Keys`, `Values`, and missing
lookups track the collection. Layout and child-component reads count too.
See [page dependencies](../incremental-builds/#understand-page-dependencies).

### Markdown APIs

Import `Kiji.Markdown` to use these extensions.

#### `UseMarkdownContent<TFrontMatter>(Action<MarkdownOptions>? configure = null)`

Recursively loads `*.md` files and registers
`ContentDictionary<MarkdownContent<TFrontMatter>>`. `TFrontMatter` must be a class. Each
file must begin with YAML front matter; the body is rendered on demand.

#### `UseMarkdownContent<TFrontMatter,TModel>(Func<MarkdownContent<TFrontMatter>,TModel> select, Action<MarkdownOptions>? configure = null)`

Projects each parsed file independently and registers `ContentDictionary<TModel>`. Both
generic arguments must be classes, and `select` cannot be null. The projection should not
depend on other items in the collection.

#### `MarkdownOptions`

The configure callback receives a new options instance for that registration.

| Member                                               | Default/behavior                                                                                                                |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `string Directory`                                  | `"contents"`; absolute or relative to the site project directory. Registered directories are watched during development.                         |
| `Func<FileInfo,bool>? FileFilter`                    | `null`; include every discovered Markdown file. The predicate runs before loading.                                              |
| `ConfigureMarkdown(Action<MarkdownPipelineBuilder>)` | Adds a non-null Markdig configuration after Kiji's default advanced pipeline. Calls run in registration order.                  |
| `ConfigureYaml(Action<DeserializerBuilder>)`         | Adds a non-null YamlDotNet configuration after the camel-case and ignore-unmatched defaults. Calls run in registration order.   |
| `AddHtmlPostProcessor(Func<string,string>)`          | Adds a non-null synchronous HTML transformation. Transformations run in registration order, each receiving the previous result. |

Local images get responsive variants and dimensions to reserve layout space. The first
loads eagerly; later images load lazily and use automatic sizing where supported, with
a viewport fallback. Use explicit HTML or Razor for finer control. Remote images,
site-root images, and explicit markup are not processed.

#### `MarkdownContent<TFrontMatter>`

Kiji creates one instance per source file.

| Member                                                                         | Behavior                                                                                                                                                                  |
| ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `FileInfo FileInfo`                                                            | Metadata for the source Markdown file.                                                                                                                                    |
| `TFrontMatter FrontMatter`                                                     | The parsed YAML object.                                                                                                                                                   |
| `ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)` | Renders the body to HTML and materializes referenced local-image variants beside the current page. Honors cancellation and propagates rendering, image, and I/O failures. |

`RenderAsync` should be called while rendering a page when the Markdown contains local
images, because their output directory comes from the current page.

### Image processing

The public image contracts are in `Kiji.Assets`.

The default processor uses SkiaSharp to generate static WebP variants from JPEG, PNG,
GIF, and WebP inputs. Animated inputs use only the first frame. It converts colors to
sRGB and drops source EXIF, IPTC, and XMP metadata. SkiaSharp's native libraries for
Windows, macOS, and Linux are included as package dependencies; no license key is needed.

#### `UseImageProcessor(Func<IImageProcessor> factory)`

Replaces the default responsive-image processor. The last registration wins. The factory
is required, runs lazily, and must return a new non-null instance. Kiji owns that instance
and disposes it with the site, using asynchronous disposal when supported.

One instance is shared by concurrent page renders and survives content reloads, so an
implementation must support concurrent calls and retain no page or content state. Declare
external configuration with `AddBuildInput` and include configuration and encoder versions
in `IImageProcessor.CacheIdentity`. This identity covers all transformation settings and
the encoder implementation. Its default value is `null`, disabling persistent reuse.

#### `IImageProcessor.ProcessAsync`

```csharp
Task<ProcessedImageInfo> ProcessAsync(
    string sourceFilePath,
    string outputDirectory,
    CancellationToken cancellationToken = default);
```

The processor should observe the token, write every returned variant into
`outputDirectory` using file names relative to that directory, and return only after the
files are ready.

#### `ProcessedImageInfo`

Construct this record with an object initializer. `OriginalWidth` and `OriginalHeight` are
required source dimensions in pixels. `Variants` is a required `IReadOnlyList<ImageVariant>`
ordered by ascending width; the largest variant is used as the default image source.

#### `ImageVariant`

`ImageVariant(string FileName, int Width)` describes one generated file. `FileName` is
relative to the output directory and `Width` is its pixel width. Both positional
properties are read-only.

## Build inputs and controls

### `AddBuildInput(string path)`

Declares a file or directory outside Kiji's known inputs. A relative path uses
the site project directory. Changes require a full rebuild; the development server also
watches declared paths. The path cannot be null, empty, or whitespace.

### `AddBuildInput(string key, string value)`

Declares a named value such as a remote-data version or encoder setting. Changing either
the key/value input set or a value requires a full rebuild. The key cannot be blank and
the value cannot be null.

### `AddPageInput(string key, Func<string> read)` and `PageBuildInputs`

Registers an external value evaluated once per build snapshot when needed.
Inject `PageBuildInputs` and call `Read(key)` during rendering; only readers depend on it.

| Method                                | Behavior                                                                                                                                                                    |
| ------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `string Read(string key)`             | Instance method; reads a registered value and records it for the current page. An unknown key throws.                                                                       |
| `static byte[] ReadFile(string path)` | Reads and records the exact file bytes consumed during rendering. Pass an absolute path resolved against the site root; a relative path uses the process working directory. |
| `static void DisableCache()`          | Disables persistent HTML reuse for the current page render, for untracked or nondeterministic inputs.                                                                       |

Call these methods during rendering to record dependencies for that page. File paths
outside the site root disable persistent HTML reuse for that page. These APIs do not
register file watchers or poll external systems. `AddBuildInput` continues to declare
dependencies shared by all pages and watches declared paths in development. See the
[input selection guide](../incremental-builds/#declare-inputs-outside-the-built-in-content-pipeline).

## Generated files

### Feeds

Import `Kiji.Feeds`.

#### `AddRssFeed(Func<IServiceProvider,IEnumerable<FeedItem>> items, string path = "feed.xml")`

Registers an RSS 2.0 artifact. The deferred factory runs when artifacts are written and
can resolve content dictionaries. Entries are emitted in supplied order. `path` is the
output-relative feed path and cannot be blank. Channel title, description, language, and
absolute URLs come from `SiteInfo`.

#### `FeedItem`

Construct an item with
`FeedItem(string Title, string Description, DateTimeOffset PublishedAt, string RelativePath)`.

The four arguments become read-only properties. Title and description must be non-null;
publication time is written in UTC. `RelativePath` supplies the link and GUID under
`SiteInfo.BaseUrl`: use a site-relative path with no leading slash, absolute URI,
backslash, query, or fragment. Use `""` for the home page.

### Sitemaps

Import `Kiji.Sitemaps`:

```csharp
app.AddSitemap(
    path: "sitemap.xml",
    excludedPaths: ["preview/", "internal/status/"]);
```

`AddSitemap(string path = "sitemap.xml", IEnumerable<string>? excludedPaths = null)`
registers a sitemap containing generated page URLs, sorted by site-relative path.
`404.html` is always excluded. Additional exclusions match relative page paths
case-insensitively and must follow the same site-relative path rules as `FeedItem`.
`path` cannot be blank.

### Custom artifacts

#### `AddArtifact(string outputRelativePath, Func<Stream,SiteOutputContext,CancellationToken,Task> write)`

Registers a site-wide output file written after pages. The path and delegate are required.
The delegate receives a writable stream owned by Kiji, the completed site context, and the
run's cancellation token. Do not dispose the stream. Output-path collisions or paths that
escape the output directory fail generation.

#### `SiteOutputContext`

Kiji constructs this type for artifact writers; it has no public constructor.

| Property                            | Behavior                                                                                                   |
| ----------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `SiteInfo Site`                     | Metadata for this generation.                                                                              |
| `IReadOnlyList<SitePageInfo> Pages` | Every generated page.                                                                                      |
| `IServiceProvider Services`         | Site services available at artifact generation time, including content dictionaries but not page services. |

#### `SitePageInfo`

`SitePageInfo(string RelativePath, string OutputRelativePath)` describes a generated page.
`RelativePath` is URL-facing and relative to `SiteInfo.BaseUrl`; it follows the site-relative
path validation rules. `OutputRelativePath` is the file-facing path below the output
directory and cannot be blank. Both values are exposed as read-only properties with the
same names.
