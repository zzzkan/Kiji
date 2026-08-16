---
title: Concepts
description: Pages, the document shell, layouts, content collections, and artifacts.
order: 20
---

## The document shell

Kiji renders the document itself: the doctype, `<html lang>` from `SiteInfo.Language`,
`<head>`, and `<body>`. You never write a layout file containing `<!DOCTYPE html>`.

Pages contribute to `<head>` through the `Kiji.Components.HeadContent` component:

```razor
<Kiji.Components.HeadContent>
    <meta charset="utf-8" />
    <title>@Title</title>
    <link rel="canonical" href="@NavigationManager.Uri" />
</Kiji.Components.HeadContent>
```

Render at most one per page — when several appear, the last one rendered wins.

## Route-declared pages

`MapPages()` scans your assembly for public components carrying a route template and
registers each as a page. Pages in another assembly register via `MapPages(assembly)`.

Routes are static or take parameters:

```razor
@page "/blog/{Slug}/"
```

Parameterized routes need their values supplied by `MapRoutes`, which is where the route
set comes from. Catch-all routes, route constraints, optional parameters, and composite
segments are rejected — a static site has to know every URL up front.

Pages are written as `route/index.html`. `dev` and `preview` resolve `/route` and
`/route/` to the same page without redirecting, matching how static hosts behave, and
canonical URLs use the trailing-slash form.

## Layouts

`MapDefaultLayout<TLayout>()` applies a layout to every page. A page opts out or swaps
with `@layout`, and layouts nest by declaring their own. A circular chain throws rather
than hanging.

```razor
@inherits LayoutComponentBase

<header>…</header>
<main>@Body</main>
```

## Content collections

`AddMarkdownContent<TFrontMatter>()` and `AddContentSource<T>(...)` declare collections
that materialize lazily. The handle they return is the single thing you pass around:

```csharp
var posts = builder.AddMarkdownContent<PostFrontMatter>()
    .WithKey(post => post.FileInfo.RelativeDirectoryPath)
    .OrderByDescending(post => post.FrontMatter.CreatedAt);
```

That same handle feeds route mappings, feeds, and component injection:

```razor
@inject ContentCollection<MarkdownContent<PostFrontMatter>> Posts
```

Passing the handle explicitly rather than resolving it from DI by type keeps two
collections of the same element type unambiguous.

Collections also support `Map` for projecting into your own type, which carries the
originating file along with each item — that provenance is what lets incremental builds
reduce a `GetRequired(key)` lookup to a dependency on one file rather than the whole
content set.

## Artifacts

RSS feeds and sitemaps are opt-in:

```csharp
app.MapFeed(posts, post => new FeedItem(post.FrontMatter.Title!, post.FrontMatter.Description!, post.FrontMatter.CreatedAt!.Value));
app.MapSitemap();
```

Anything else site-wide implements `ISiteArtifact` and registers with
`app.MapArtifact(...)`. Artifacts run after all pages and see every page's metadata.

## Base paths

`SiteInfo.BaseUrl` may include a path segment, for a site published under a sub-path such
as a GitHub Pages project site. Write your own links through `Site.Path`:

```razor
<a href="@Site.Path("docs/")">Docs</a>
<link rel="stylesheet" href="@Site.Path("css/app.css")" />
```

Canonical, feed, and sitemap URLs already carry the prefix, since they derive from
`BaseUrl`. So do markdown page-bundle images, which are relative to the page that uses
them. See [Deployment](../deployment/) for the details.

## Interactivity

There isn't any, by design. Kiji renders through `HtmlRenderer`, which is one-shot static
rendering: `OnInitialized` and `OnParametersSet` run, `OnAfterRenderAsync` does not, and
event handlers like `@onclick` do not survive into the output. The generated site is
plain HTML with no Blazor runtime.

If you want client-side behavior, write JavaScript and put it in `wwwroot/`. If you want
a real Blazor app, you want Blazor WebAssembly, not a static site generator.
