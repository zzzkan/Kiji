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
can identify only one registered dictionary. This form has no data digest, so pages
reading its items are rendered on every publish.

### `UseContentSource<T>(string sourceId, Func<IServiceProvider, IReadOnlyList<ContentEntry<T>>> loader)`

Registers a source with stable, source-local identities. Return
`new ContentEntry<T>(id, value, digest)` for each item. The digest must change whenever
any data affecting the value changes, including data obtained from HTTP or other sources.
Use `null` when that guarantee cannot be made; only readers of that item lose HTML reuse.
Values are immutable for one build snapshot. Collection enumeration tracks item order,
membership, and all item digests. A missing lookup tracks the collection too.

### `AddPageInput(string key, Func<string> read)` and `PageBuildInputs`

Registers an external value evaluated once per build snapshot when needed.
Inject `PageBuildInputs` and call `Read(key)` during rendering; only readers depend on it.
Use `PageBuildInputs.ReadFile(path)` to read bytes and track their content, or
`PageBuildInputs.DisableCache()` for untracked, nondeterministic inputs.
File paths outside the site root disable portable HTML reuse for that page.
`AddBuildInput` continues to declare dependencies shared by all pages.

### `ContentDictionary<T>`

`ContentDictionary<T>` implements `IReadOnlyDictionary<string, T>` and is resolved through
dependency injection. It has no public constructor. Keys are case-insensitive opaque
lookup values. Markdown keys are source-relative paths using `/`, independent of the
checkout directory. Custom sources with explicit entries supply their own stable IDs.
Keys do not determine public URLs.

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
in `IImageProcessor.CacheIdentity`. This identity covers all transformation settings and
the encoder implementation. Its default value is `null`, disabling persistent reuse.

### `IImageProcessor.ProcessAsync`

```csharp
Task<ProcessedImageInfo> ProcessAsync(
    string sourceFilePath,
    string outputDirectory,
    CancellationToken cancellationToken = default);
```

The method receives the source image, an isolated output directory, and the run's
cancellation token. Kiji owns persistence and copies variants into page bundles. It must
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


## Incremental publishing cache

Kiji stores page dependencies and SHA-256 hashes in `.kiji/cache/manifest.json`,
with reusable HTML in the single `html-*.bin` file it references. Generated images use separate SHA-256 blobs under
`.kiji/cache/images`; image generation records contain dimensions and variant mappings. Restoring `.kiji/cache`
alone is sufficient to reuse unchanged outputs on a clean checkout. The publish staging
directory stays under `obj`; static assets are copied from source, and RSS/sitemap are
generated on each publish.

Reuse compares content digests, route parameters, declared external inputs, and code
dependencies. Code identity uses the compiler-generated module version ID (MVID) of
the site and its referenced assemblies, including framework code. A changed MVID
invalidates all HTML; content changes invalidate its readers. Dynamic assemblies or
unavailable code identities cannot reuse HTML. Existing caches using binary or compilation-input
hashes are rebuilt once when switching to MVIDs.

Release site executables use deterministic compilation without PDBs, omit the source
revision from informational versions, and map the project directory to `/_/source`.
These settings stabilize code identity across checkouts and content-only commits.
They apply to Release builds as well as publishes, including `build` followed by
`publish --no-build`, independently of `KijiGenerateOnPublish`. Use Debug for source-level
debugging; Debug builds, libraries, and tests keep their existing debug settings.
Explicit MSBuild global properties retain their normal precedence.

MSBuild owns recompilation. Kiji does not detect timestamp-preserving edits to C# or
Razor sources before compilation, or post-compilation DLL edits that retain the MVID.
Markdown and other content inputs are still checked by content hash. Referenced
projects do not inherit the site's compilation settings: configure them separately
when stable identities across checkouts are needed. Different compiler versions,
generated code, assembly versions, or dependency MVIDs can require a full rebuild.

Inputs and cached HTML are checked by content hash. Generated HTML and images are exclusively owned
by Kiji: in the same output directory, matching size and last-write time allow reuse
without reading it again. External edits that preserve both are outside this guarantee.
Changed stamps or a different output directory trigger content verification. Restored
image blobs are also checked by content hash before use.
Missing or corrupt HTML is rendered again. Missing or corrupt images can be regenerated
without rendering unchanged HTML. Custom code must declare external dependencies; Kiji
cannot observe arbitrary file, HTTP, reflection, or clock access.

Successful builds write a complete HTML bundle before atomically replacing the manifest,
then collect the previous bundle, unreferenced blobs, and
image records. Failed builds keep the last successful manifest. Build transactions sharing
a cache are serialized; pages still render concurrently. Cache size follows the current
site's reusable HTML and unique image outputs, rather than the number of builds. Pages
share one HTML bundle to avoid thousands of small cache-file operations. Reused pages
share slices of that bundle in memory. Code changes cause all pages to render and do
not load the previous HTML bundle. Unchanged builds retain both files. Restoring output
updates only the manifest's output stamps and retains the HTML bundle. Uncacheable pages store only metadata.

The development mirror, including generated images, lives under `.kiji/dev-site`, separate from the publish cache in `.kiji/cache`.
Development does not write to the publish cache in `.kiji/cache`.
`-p:KijiForce=true` renders all pages; `dotnet clean` removes generated caches.
Old manifest formats are not migrated.
