---
title: API reference
description: Every public Kiji type, member, default, and execution contract.
order: 100
---

This page covers Kiji's complete public site-authoring API. All `StaticSite` registration
methods return the same site instance and may be chained. Configure the site before
`RunAsync`; execution makes its configuration read-only.

## StaticSite lifecycle

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

### `StaticSite.Create(string[] args)`

Creates one site. `args` cannot be null and are forwarded to ASP.NET Core configuration
when the development server runs. Each `StaticSite` instance can execute once.

### `StaticSite.Info`

Gets or sets the required `SiteInfo`. Reading it before assignment throws. It may be
reassigned until execution starts; setting it afterward throws.

### `StaticSite.Paths`

Gets the site's `SitePaths`. The object is created with the site and remains mutable until
execution starts.

### `StaticSite.RunAsync(CancellationToken cancellationToken = default)`

Starts the development server during `dotnet watch`, and generates the
site when invoked by Kiji's `dotnet publish` targets. Await it from the entry point and
return its process exit code. The method freezes configuration, owns the resources created
for the run, and disposes them on success, failure, or cancellation. Calling it more than
once throws.

## SiteInfo

`SiteInfo` contains site-wide metadata used by rendered pages, feeds, and sitemaps.

| Property             | Required/default | Behavior                                                                                                                                        |
| -------------------- | ---------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| `Uri BaseUrl`        | Required         | Must be an absolute HTTP or HTTPS URL without a query or fragment. It is normalized to end in `/`; its path is the site's deployment base path. |
| `string Name`        | Required         | Must not be null, empty, or whitespace. Used as site metadata and by RSS.                                                                       |
| `string Description` | `""`             | Site description and default RSS channel description.                                                                                           |
| `string Language`    | `"en"`           | Written to `<html lang>` and the RSS channel language.                                                                                          |
| `string Author`      | `""`             | Author metadata available to the site.                                                                                                          |

All properties are initialized when the `SiteInfo` is constructed.

## SitePaths

`SitePaths` configures input directories. Kiji creates it as `StaticSite.Paths`; it has no
public constructor. Relative paths are resolved from `RootDirectory`.

| Property                  | Default                                                                  | Behavior                                                                                    |
| ------------------------- | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------- |
| `string RootDirectory`    | Nearest project directory, then nearest Git root, then current directory | Base for relative site paths.                                                               |
| `string ContentDirectory` | `contents`                                                               | Root used by content sources.                                                               |
| `string StaticDirectory`  | `wwwroot`                                                                | Directory copied unchanged to generated output. It may be absolute or relative to the root. |

Changing a property after execution starts throws.

## Pages, layouts, and page services

### `AddStaticPages()`

Finds public, non-abstract components with fixed routes in the entry assembly. Components
with parameterized routes are ignored. If there is no entry assembly, use the assembly
overload.

### `AddStaticPages(Assembly assembly)`

Finds fixed routed components in the supplied non-null assembly. Registering the same
assembly repeatedly has no additional effect.

### `AddPages<TPage>(Func<IServiceProvider, IEnumerable<object>> parameters)`

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

### `UseDefaultLayout<TLayout>()`

Sets the `LayoutComponentBase` used by pages without their own `@layout`. A page's layout
takes precedence, and layouts may nest through their own `@layout`. Without a default,
the page renders directly inside the generated document body.

### `UseNotFoundPage<TComponent>()`

Writes a routed `IComponent` as `/404.html`. The component must declare exactly one route
and cannot also be registered through `AddPages`.

### `AddPageService<T>()`

Registers one instance of a concrete class for each page render. Its public constructor
dependencies are resolved automatically; the page, layout, and child components share the
instance. Kiji disposes it when that render finishes.

The type must be a non-abstract class with a public constructor and may be registered only
once. Page services are unavailable to content loaders, route/feed factories, and artifact
writers.

## HeadContent

`Kiji.Components.HeadContent` is a component that places its `RenderFragment? ChildContent`
inside the generated document `<head>`:

```razor
<Kiji.Components.HeadContent>
    <meta charset="utf-8" />
    <title>@Title</title>
    <link rel="canonical" href="@NavigationManager.Uri" />
</Kiji.Components.HeadContent>
```

It renders no markup at its position in the body. If more than one instance renders on a
page, the most recently rendered instance supplies the head content.

## Content sources and ContentDictionary

### `UseContentSource<T>(Func<IServiceProvider, IReadOnlyList<T>> loader)`

Registers a content source for reference type `T`. The loader runs lazily once for each
site snapshot, after execution paths are known. Its items receive opaque string keys based
on their zero-based order and are exposed as `ContentDictionary<T>`. A given element type
can identify only one registered dictionary.

### `ContentDictionary<T>`

`ContentDictionary<T>` implements `IReadOnlyDictionary<string, T>` and is resolved through
dependency injection. It has no public constructor. Keys are case-insensitive opaque
lookup values; they are not URLs, slugs, or stable domain identifiers.

| Member                                                | Behavior                                                                                     |
| ----------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| `int Count`                                           | Number of items.                                                                             |
| `IEnumerable<string> Keys`                            | Keys in source order.                                                                        |
| `IEnumerable<T> Values`                               | Items in source order.                                                                       |
| `T this[string key]`                                  | Returns the case-insensitive match; throws `KeyNotFoundException` when absent.               |
| `bool ContainsKey(string key)`                        | Returns whether the key exists; a null key throws.                                           |
| `bool TryGetValue(string key, out T? item)`           | Performs a case-insensitive lookup; returns `false` and null when absent. A null key throws. |
| `IEnumerator<KeyValuePair<string,T>> GetEnumerator()` | Enumerates entries in source order; non-generic enumeration uses the same entries.           |

During a tracked page render, a successful keyed lookup observes that item. Inspecting the
dictionary's shape through `Count`, `Keys`, `Values`, enumeration, or a missing lookup
observes the collection, so incremental publishing can rebuild pages when that shape
changes.

## Markdown APIs

The public Markdown extensions are declared by `Kiji.Markdown.MarkdownStaticSiteExtensions`.

### `UseMarkdownContent<TFrontMatter>(Action<MarkdownOptions>? configure = null)`

Recursively loads `*.md` files and registers
`ContentDictionary<MarkdownContent<TFrontMatter>>`. `TFrontMatter` must be a class. Each
file must begin with YAML front matter; the body is rendered on demand.

### `UseMarkdownContent<TFrontMatter,TModel>(Func<MarkdownContent<TFrontMatter>,TModel> select, Action<MarkdownOptions>? configure = null)`

Projects each parsed file independently and registers `ContentDictionary<TModel>`. Both
generic arguments must be classes, and `select` cannot be null. The projection should not
depend on other items in the collection.

### `MarkdownOptions`

The configure callback receives a new options instance for that registration.

| Member                                               | Default/behavior                                                                                                                |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `string? Directory`                                  | `null`; scan the content root. A value selects a directory below it and cannot escape the content root.                         |
| `Func<FileInfo,bool>? FileFilter`                    | `null`; include every discovered Markdown file. The predicate runs before loading.                                              |
| `ConfigureMarkdown(Action<MarkdownPipelineBuilder>)` | Adds a non-null Markdig configuration after Kiji's default advanced pipeline. Calls run in registration order.                  |
| `ConfigureYaml(Action<DeserializerBuilder>)`         | Adds a non-null YamlDotNet configuration after the camel-case and ignore-unmatched defaults. Calls run in registration order.   |
| `AddHtmlPostProcessor(Func<string,string>)`          | Adds a non-null synchronous HTML transformation. Transformations run in registration order, each receiving the previous result. |

### `MarkdownContent<TFrontMatter>`

This type has no public constructor. Kiji creates it for a source file.

| Member                                                                         | Behavior                                                                                                                                                                  |
| ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `FileInfo FileInfo`                                                            | Metadata for the source Markdown file.                                                                                                                                    |
| `TFrontMatter FrontMatter`                                                     | The parsed YAML object.                                                                                                                                                   |
| `ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)` | Renders the body to HTML and materializes referenced local-image variants beside the current page. Honors cancellation and propagates rendering, image, and I/O failures. |

`RenderAsync` should be called while rendering a page when the Markdown contains local
images, because their output directory comes from the current page.

## Build inputs and controls

### `AddBuildInput(string path)`

Declares a file or directory outside Kiji's known inputs. A relative path uses
`SitePaths.RootDirectory`. Changes require a full rebuild; the development server also
watches declared paths. The path cannot be null, empty, or whitespace.

### `AddBuildInput(string key, string value)`

Declares a named value such as a remote-data version or encoder setting. Changing either
the key/value input set or a value requires a full rebuild. The key cannot be blank and
the value cannot be null.

## Feeds

The public feed types are in `Kiji.Feeds`; `AddRssFeed` is declared by
`RssFeedStaticSiteExtensions`.

### `AddRssFeed(Func<IServiceProvider,IEnumerable<FeedItem>> items, string path = "feed.xml")`

Registers an RSS 2.0 artifact. The deferred factory runs when artifacts are written and
can resolve content dictionaries. Entries are emitted in supplied order. `path` is the
output-relative feed path and cannot be blank. Channel title, description, language, and
absolute URLs come from `SiteInfo`.

### `FeedItem`

Construct an item with
`FeedItem(string Title, string Description, DateTimeOffset PublishedAt, string RelativePath)`.

| Property                     | Behavior                                                                                                                                         |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `string Title`               | Required entry title.                                                                                                                            |
| `string Description`         | Required entry description.                                                                                                                      |
| `DateTimeOffset PublishedAt` | Written as the RSS publication time in UTC.                                                                                                      |
| `string RelativePath`        | Combined with `SiteInfo.BaseUrl` for the link and GUID. It must be site-relative: no leading slash, absolute URI, backslash, query, or fragment. |

Null titles or descriptions and invalid relative paths throw during construction.

## Sitemaps

`AddSitemap` is declared by `Kiji.Sitemaps.SitemapStaticSiteExtensions`:

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

## Custom artifacts

### `AddArtifact(string outputRelativePath, Func<Stream,SiteOutputContext,CancellationToken,Task> write)`

Registers a site-wide output file written after pages. The path and delegate are required.
The delegate receives a writable stream owned by Kiji, the completed site context, and the
run's cancellation token. Do not dispose the stream. Output-path collisions or paths that
escape the output directory fail generation.

### `SiteOutputContext`

Kiji constructs this type for artifact writers; it has no public constructor.

| Property                            | Behavior                                                                                                   |
| ----------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `SiteInfo Site`                     | Metadata for this generation.                                                                              |
| `IReadOnlyList<SitePageInfo> Pages` | Every generated page.                                                                                      |
| `IServiceProvider Services`         | Site services available at artifact generation time, including content dictionaries but not page services. |

### `SitePageInfo`

`SitePageInfo(string RelativePath, string OutputRelativePath)` describes a generated page.
`RelativePath` is URL-facing and relative to `SiteInfo.BaseUrl`; it follows the site-relative
path validation rules. `OutputRelativePath` is the file-facing path below the output
directory and cannot be blank. Both values are exposed as read-only properties with the
same names.

## Image processing

The public image contracts are in `Kiji.Assets`.

### `UseImageProcessor(Func<IImageProcessor> factory)`

Replaces the default responsive-image processor. The last registration wins. The factory
is required, runs lazily, and must return a new non-null instance. Kiji owns that instance
and disposes it with the site, using asynchronous disposal when supported.

One instance is shared by concurrent page renders and survives content reloads, so an
implementation must support concurrent calls and retain no page or content state. Declare
external configuration with `AddBuildInput` and include configuration and encoder versions
in any custom cache identity.

### `IImageProcessor.ProcessAsync`

```csharp
Task<ProcessedImageInfo> ProcessAsync(
    string sourceFilePath,
    string outputDirectory,
    string? cacheDirectory = null,
    CancellationToken cancellationToken = default);
```

The method receives the source image, the directory where the current page's variants
belong, an optional persistent cache directory, and the run's cancellation token. It must
write every returned variant into `outputDirectory`, use file names relative to that
directory, and return only after the files are ready.

### `ProcessedImageInfo`

Construct this record with an object initializer. `OriginalWidth` and `OriginalHeight` are
required source dimensions in pixels. `Variants` is a required `IReadOnlyList<ImageVariant>`
ordered by ascending width; the largest variant is used as the default image source.

### `ImageVariant`

`ImageVariant(string FileName, int Width)` describes one generated file. `FileName` is
relative to the output directory and `Width` is its pixel width. Both positional
properties are read-only.
