---
title: Concepts
description: Pages, the document shell, layouts, content dictionaries, and artifacts.
order: 20
---

## Site structure

A Kiji site is assembled from a few core building blocks:

- `StaticSite` — the site container that owns the generation pipeline
- `SiteInfo` — site metadata such as the name, base URL, and language
- `AddStaticPages()` / `AddPages<TPage>()` — register the pages to generate
- `UseDefaultLayout<TLayout>()` — apply a common layout to pages
- `UseMarkdownContent(...)` — load Markdown as a content source
- `AddRssFeed(...)` / `AddSitemap()` — emit site-wide output artifacts

For example, a site with fixed pages, Markdown-backed posts, a shared layout, and a sitemap
can be defined like this:

```csharp
var site = StaticSite.Create(args);
site.Info = new SiteInfo
{
    Name = "My site",
    BaseUrl = new Uri("https://example.com/"),
    Language = "en",
};

site.UseMarkdownContent<PostFrontMatter>();
site.UseDefaultLayout<SiteLayout>();

site.AddStaticPages();
site.AddPages<PostPage>(services =>
    services.GetRequiredService<ContentDictionary<MarkdownContent<PostFrontMatter>>>()
        .Select(post => new
        {
            Slug = post.Value.FileInfo.Directory!.Name,
            ContentKey = post.Key,
        }));
site.AddSitemap();

return await site.RunAsync();
```

You can start small and grow the site from there: define the site, register pages and content,
then run it.

## Routing

Kiji supports two ways to turn routed components into generated pages: `AddStaticPages` discovers
fixed routes from an assembly, while `AddPages<TPage>` supplies the parameter sets for a
parameterized route. Both forms write pages as `route/index.html`.

### Fixed routes

`AddStaticPages()` finds components with fixed routes in the entry assembly and registers them as
pages. For example:

```csharp
site.AddStaticPages();
```

```razor
@page "/about/"

<h1>About</h1>
```

This generates `about/index.html`. Use `AddStaticPages(assembly)` when the pages are defined in
another assembly. Parameterized routes use `AddPages<TPage>` instead.

### Parameterized routes

`AddPages<TPage>` registers parameter sets for a component with one parameterized route.
No assembly scan is required. Route names bind to the component's `[Parameter]` properties;
additional properties are passed to the component without appearing in the URL.

For example, this creates one page per slug:

```csharp
site.AddPages<PostPage>(_ => new[]
{
    new { Slug = "first-post" },
    new { Slug = "second-post" },
});
```

```razor
@page "/blog/{Slug}/"
@code {
    [Parameter] public string Slug { get; set; } = string.Empty;
}
```

Values keep their original .NET types; Kiji does not perform implicit conversions. Route
values must be nonempty and fit one path segment. Incorrect names, types, and non-public
setters fail during page planning. Catch-all routes, constraints, optional parameters, and
composite segments are not supported because a static site must know every output URL while
it is being planned.

`AddPages<TPage>` evaluates its parameter factory once per registration per snapshot, after
execution paths are settled. Multiple registrations for the same type concatenate their
results; an empty result is allowed. Invalid parameters and duplicate output paths fail
during page planning.

### Not-found pages

Register a component as the site's not-found page with `UseNotFoundPage<TPage>()`:

```csharp
site.UseNotFoundPage<NotFoundPage>();
```

The component needs one route declaration, just like any other page:

```razor
@page "/not-found/"

<h1>Page not found</h1>
<p>The page you requested does not exist.</p>
<a href="@Site.BaseUrl.AbsolutePath">Go to the home page</a>
```

Kiji writes this page to `404.html`. Do not register the same component with `AddPages`.

## Document skeleton

Kiji renders the document itself: the doctype, `<html lang>` from `SiteInfo.Language`,
`<head>`, and `<body>`. You never write a layout file containing `<!DOCTYPE html>`.

Pages contribute to `<head>` through the `Kiji.Components.HeadContent` component:

```razor
<HeadContent>
    <meta charset="utf-8" />
    <title>@Title</title>
    <link rel="canonical" href="@NavigationManager.Uri" />
</HeadContent>
```

Render at most one per page — when several appear, the last one rendered wins.

## Layouts

`UseDefaultLayout<TLayout>()` applies a layout to every page. A page opts out or swaps with `@layout`, and layouts nest by declaring their own.

```razor
@inherits LayoutComponentBase

<header>…</header>
<main>@Body</main>
```

## Markdown

Markdown is an optional content source for pages, feeds, and other site features. Kiji
reads Markdown files with front matter, renders their body to HTML, and can process local
images as part of the page bundle. The resulting items are exposed as a typed content
dictionary, so a site can decide how to map content to URLs and page components.

See [Markdown and images](../markdown/) for registration, front matter, rendering,
content projections, image handling, and the extension points around them.

## Artifacts

RSS feeds and sitemaps are opt-in. The feed takes the entries themselves, in the order
they should be read:

```csharp
site.AddRssFeed(services => services
    .GetRequiredService<ContentDictionary<Post>>().Values
    .OrderByDescending(post => post.CreatedAt)
    .Select(post => new FeedItem(post.Title, post.Description, post.CreatedAt, RelativePath: $"blog/{post.Slug}/")));

site.AddSitemap();
```

The sitemap excludes `404.html` by default. Pass site-relative paths to omit other
generated pages:

```csharp
site.AddSitemap(excludedPaths: [
    "preview/",
    "internal/status/",
]);
```

Anything else site-wide registers a writer delegate with
`site.AddArtifact(outputRelativePath, write)`. Artifacts run after all pages and the writer
receives every page's metadata through `SiteOutputContext`.

## Interactivity

There isn't any, by design. Kiji renders through `HtmlRenderer`, which is one-shot static
rendering: `OnInitialized` and `OnParametersSet` run, `OnAfterRenderAsync` does not, and
event handlers like `@onclick` do not survive into the output. The generated site is
plain HTML with no Blazor runtime.

If you want client-side behavior, write JavaScript and put it in `wwwroot/`. If you want
a real Blazor app, you want Blazor WebAssembly, not a static site generator.
