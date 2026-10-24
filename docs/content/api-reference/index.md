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
- [Page reuse controls](#page-reuse-controls)
- [Generated files](#generated-files)
- [Unsupported Blazor features](#unsupported-blazor-features)

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

`StaticSite.Create` automatically discovers all `@page` routes on public, non-abstract
components in the entry assembly. Fixed routes generate one page with the component's
default parameter values. Parameterized routes require `AddPageParameters<TPage>`;
missing registration is an error before rendering.

#### `AddPageAssembly(Assembly assembly)`

Includes routed components from an additional, non-null assembly. Referenced assemblies
are not scanned automatically. Repeated registration, including the entry assembly, has
no additional effect.

#### `AddPageParameters<TPage>(Func<IServiceProvider, IEnumerable<object>> parameters)`

Adds parameter sets for the one parameterized route of an `IComponent`, or for its only
route when that route is fixed. A component can combine fixed routes with one
parameterized route; its fixed routes still generate one page each with default parameter
values. Components with several parameterized routes, or with several fixed routes and no
parameterized route, are rejected. The
component must belong to the entry assembly or an assembly added with `AddPageAssembly`;
supplying parameters does not implicitly include an assembly. Registration order does
not matter. The factory is deferred until paths and services are ready and runs once per registration per
site snapshot. Its service provider can resolve registered content dictionaries.

Each returned object is the component's complete parameter set. Anonymous objects and
string/object dictionaries are supported. Names match public writable `[Parameter]`
properties case-insensitively; route parameter names also build the URL. Values retain
their .NET types, although route values are formatted invariantly for their URL segment.
Missing or empty route values, unknown parameter names, and incompatible values fail
planning. Non-route parameters may be omitted to use the component's defaults.
`EditorRequired` is not validated by Kiji.

Multiple registrations for the same component are concatenated and an empty sequence is
allowed, explicitly generating no pages for the receiving route. For a component whose
only route is fixed, explicit parameter sets replace the default rendering; they do not
add another default page. Multiple sets for the same fixed route fail with duplicate
output paths. Treat supplied objects as read-only
for the snapshot; Kiji does not clone or dispose them. See [Routing](../routing/) for examples and supported
route templates.

HTML can be reused when parameter values are `null` or have one of these types:
`string`, `bool`, `char`, `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`,
`ulong`, enums, and `Guid`. Other types can be valid component parameters but disable
HTML reuse for that page. Prefer passing a content key and looking up the item during
rendering. See [Incremental builds](../incremental-builds/#pass-stable-page-parameters).

#### `AddNotFoundPage<TPage>()`

Writes a routed `IComponent` as `/404.html`. The component must belong to a page assembly,
declare exactly one route, and cannot also receive `AddPageParameters<TPage>`.

### Layout

#### `UseDefaultLayout<TLayout>()`

Sets the `LayoutComponentBase` used by pages without their own `@layout`. A page's layout
takes precedence, and layouts may nest through their own `@layout`. Without a default,
the page renders directly inside the generated document body.

### Services

#### `UseServices(Action<IServiceCollection> configure)`

Registers services using standard [.NET dependency injection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview).

```csharp
app.UseServices(services =>
{
    services.AddScoped<RelatedPosts>();
    services.AddLocalization();
});
```

Import `Microsoft.Extensions.DependencyInjection`. Callbacks run once in registration order
before rendering; the collection is then read-only. Use ordinary C# lambdas or named methods
for callbacks and factories; standalone runtime-generated methods are unsupported.

Components and services can inject `ILogger<T>`. Kiji writes `Warning` and higher
logs to standard error during publish and development, including exceptions caught by
[`ErrorBoundary`](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/handle-errors?view=aspnetcore-10.0#error-boundaries).

Each page render owns a DI scope: scoped services are shared by its page, layout and child
components and disposed when rendering finishes. Loaders, route/feed factories and artifact
writers cannot resolve scoped services. Kiji-managed services such as `SiteInfo`, `PageInfo`
and `ContentDictionary<>` cannot be replaced.

External service data and captured values are not tracked automatically. Use
`[AlwaysRender]` on components that read them. Content reload does not recreate
singletons; use [content sources](#content-sources-and-contentdictionary) for shared
indexes that must be rebuilt when content changes.

### Page metadata

#### PageInfo

Inject `Kiji.PageInfo` during rendering. Its read-only `Uri Url` is the page's public
URL, including the deployment base path.

```razor
@inject PageInfo Page
@inject SiteInfo Site

<HeadContent>
    <link rel="canonical" href="@Page.Url.AbsoluteUri" />
</HeadContent>
<a href="@Site.ResolveUrl("about/").AbsolutePath">About</a>
```

#### PageTitle, HeadContent, and HeadOutlet

Use Blazor's `PageTitle` for the page title and `HeadContent` for metadata, stylesheets,
and scripts. Kiji's built-in `HeadOutlet` places this content in the document's `<head>`.

```razor
<PageTitle>@Title</PageTitle>

<HeadContent>
    <meta charset="utf-8" />
    <meta name="description" content="@Description" />
    <link rel="stylesheet" href="@Assets["css/app.css"]" />
</HeadContent>
```

Kiji waits for asynchronous rendering to finish, so the generated HTML includes the updated title and metadata.

### Static assets

The inherited `ComponentBase.Assets` property resolves paths such as `Assets["css/app.css"]`
to public URLs, including fingerprints when available and `SiteInfo.BaseUrl`'s path prefix.
An unknown path is returned unchanged. Literal URLs in CSS, JavaScript, or HTML are not
automatically rewritten. Use `SiteInfo.ResolveUrl` for links to pages and generated files.

Blazor's `ImportMap` component supplies an import map for the site's static assets.
Place `<ImportMap />` inside `HeadContent` before scripts that use JavaScript imports.
Its `ImportMapDefinition` parameter accepts a custom map. See the
[Blazor import map reference](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0#importmap-component).

See [Concepts](../concepts/#static-assets) for asset links and component styles,
[Incremental builds](../incremental-builds/#what-changes-cause-work) for rebuild behavior,
and [Deployment](../deployment/#compression) for compression settings.

## Content and images

### Content

#### `ContentDictionary<T>`

An injectable, read-only collection of site content. `UseMarkdownContent<FrontMatter>()`
registers `ContentDictionary<MarkdownContent<FrontMatter>>` for components and factories
to read. For a Markdown site, this is usually all the content registration you need.

```razor
@inject ContentDictionary<MarkdownContent<FrontMatter>> Posts

<ul>
    @foreach (var post in Posts.Values)
    {
        <li>@post.FrontMatter.Title</li>
    }
</ul>
```

Import `Kiji`, `Kiji.Markdown`, and your model namespace in `_Imports.razor`.
The dictionary implements `IReadOnlyDictionary<string, T>`. A successful keyed lookup
tracks one item; enumeration, `Count`, `Keys`, `Values`, and missing lookups track the
whole collection, including order and item digests. Layout and child-component reads
count too. See [page dependencies](../incremental-builds/#understand-page-dependencies).

Keys are case-insensitive and enumeration preserves source order. Markdown keys are
source-relative paths using `/`; custom sources use ordinal keys or explicit IDs as
described below. Keys do not determine public URLs.

Use `UseContentSource` for custom data, such as JSON, or a shared index derived from
other content, such as tag counts. Both overloads expose `ContentDictionary<T>` and
load lazily once per site snapshot, after execution paths are known. Treat loaded values
as immutable. Each publish or development content reload invalidates them; the next
access loads them again.

#### `UseContentSource<T>(Func<IServiceProvider, IReadOnlyList<T>> loader)`

Use this form to share loaded data without supplying change detection. Items receive
keys `"0"`, `"1"`, and so on in loader order. Pages reading these items render on every
publish.

For example, use the `FrontMatter` model from [Markdown and images](../markdown/#register-markdown-content)
and define a model in its own `TagSummary.cs` file:

```csharp
public sealed record TagSummary(string Name, int Count);
```

In `Program.cs`, import `Kiji`, `Kiji.Markdown`, `Microsoft.Extensions.DependencyInjection`,
and your model namespace. Register Markdown and the shared tag counts before `RunAsync`:

```csharp
app.UseMarkdownContent<FrontMatter>();
app.UseContentSource<TagSummary>(LoadTags);

static IReadOnlyList<TagSummary> LoadTags(IServiceProvider services) =>
    services.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
        .Values
        .SelectMany(post => post.FrontMatter.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
        .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => new TagSummary(group.Key, group.Count()))
        .ToArray();
```

Components can inject `ContentDictionary<TagSummary>` and render its `Values`. The
counts are shared across pages and recomputed after Markdown content reloads.

#### `UseContentSource<T>(string sourceId, Func<IServiceProvider, IReadOnlyList<ContentEntry<T>>> loader)`

Use this form when pages should be eligible for HTML reuse if the data is unchanged.
Replace the `UseContentSource` registration above with this one:

```csharp
app.UseContentSource<TagSummary>("tag-summary", services =>
    LoadTags(services)
        .Select(tag => new ContentEntry<TagSummary>(
            Id: tag.Name,
            Value: tag,
            Digest: tag.ToString()))
        .ToArray());
```

Keep `sourceId` stable between builds. Each entry's `Id` identifies the item within
that source, independently of its position. `Digest` is a comparison string that must
change whenever any data affecting the value changes, including external data. Here,
the record's generated `ToString()` includes both values, for example
`TagSummary { Name = dotnet, Count = 3 }`.

An incorrect digest can reuse stale HTML. Use `null` when a reliable digest is unavailable;
readers of that item or its collection then lose HTML reuse. Digests allow Kiji to skip
page rendering, but the loader still runs when needed to obtain current digests.

Use [`AlwaysRender`](../incremental-builds/#render-pages-that-use-external-data) when a
component also reads untracked external state. It forces page rendering but does not
refresh data cached in a service.

### Markdown

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
| `string Directory`                                   | `"content"`; absolute or relative to the site project directory. Registered directories are watched during development.         |
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

The default processor uses SkiaSharp to generate static WebP variants from JPEG, PNG,
GIF, and WebP inputs. Animated inputs use only the first frame. It converts colors to
sRGB and drops source EXIF, IPTC, and XMP metadata.

To replace it, register `IImageProcessor` from `Kiji.Assets` as a singleton:

```csharp
app.UseServices(services =>
    services.AddSingleton<IImageProcessor, MyImageProcessor>());
```

Custom processors must support concurrent calls and retain no page or content state.
Set `CacheIdentity` to identify the implementation and settings for persistent reuse;
its default `null` disables reuse. Include external configuration and encoder versions
in that identity; page rendering controls do not invalidate image processing caches.

## Page reuse controls

### `AlwaysRenderAttribute`

Add `@attribute [AlwaysRender]` to a Razor component, or `[AlwaysRender]` to a component
class, using the `Kiji` namespace. Every page that renders the component is regenerated
on each publish, including uses in layouts and child components. The attribute is inherited.

Use it for untracked external state. For a one-time refresh of all pages, use
`dotnet publish -p:KijiForce=true`. See the [external data guide](../incremental-builds/#render-pages-that-use-external-data)
for an example and development behavior.

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

## Unsupported Blazor features

Kiji generates static HTML without a Blazor runtime. Event handlers such as
`@onclick` do not run in the browser.

### NavigationManager and NavLink

Kiji provides `NavigationManager` injection and static `NavLink` rendering for Razor
class library (RCL) compatibility. Prefer `PageInfo`, `SiteInfo`, and ordinary `<a>`
links in components written for Kiji.

Each render uses `SiteInfo.BaseUrl` for `BaseUri` and `PageInfo.Url` for `Uri`,
including in development. Request queries are excluded; 404 pages use their own route.
`NavLink` can render its active class, but Kiji does not rewrite `href` or add `<base>`.

This is not full static SSR: `NavigateTo`/`Refresh` throw `NotSupportedException`,
and `NotFound()` does not set HTTP status or select Kiji's 404 page.
Client-side routing and navigation interception are unavailable. Configure redirects
in your hosting platform.

### JavaScript interop

Components and page services can inject `IJSRuntime`, but interop calls throw
`InvalidOperationException`. Its registration is managed by Kiji.

`OnAfterRender` and `OnAfterRenderAsync` do not run. Components that defer interop
to these methods can render static HTML. Use a `<script>` element or JavaScript
module for browser behavior.
