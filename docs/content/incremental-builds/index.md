---
title: Incremental builds
description: Reuse unchanged pages and control page rendering.
order: 60
---

The first `dotnet publish` renders every page. Later publishes reuse a page's HTML when
nothing it depends on has changed, and always produce a complete site.

## What changes cause work?

| Change | Work |
| --- | --- |
| Markdown changes | Render pages that read that item or its collection. |
| Content is added, removed, or reordered | Render pages that read the whole collection, and add or remove pages. |
| Page parameters change | Render affected pages. |
| A local image changes | Render pages that use it and update its variants. |
| A static asset changes | Update the asset and pages that link it through `Assets` or `ImportMap`. |
| Code, packages, the .NET runtime, or site settings change | Render all pages. |
| A page uses an `AlwaysRender` component | Render that page on every publish. |

Feeds, sitemaps, and custom artifacts are regenerated on every publish.

## Page dependencies

`Posts[ContentKey]` depends on one item. Enumerating a dictionary, reading `Count`, `Keys`,
or `Values`, or looking up a missing key depends on the whole collection, even if you
filter afterward. Reads in layouts and child components count too: a layout that lists
every post makes every page depend on all posts.

Pass a content key or another simple value through `AddPageParameters` and look the item
up while rendering. Passing whole models or arrays works but disables reuse for that page;
see the [reusable parameter types](../api-reference/#pages).

## Render pages that use external data

Kiji tracks reads through `ContentDictionary<T>`. To track other data, load it with
[AddContentSource](../api-reference/#content-sources-and-contentdictionary) and supply
digests. Kiji cannot see other reads, such as files, HTTP, environment variables, the
clock, or service state. Mark components that render such values:

```razor
@attribute [AlwaysRender]

<p>@Environment.GetEnvironmentVariable("SITE_ANNOUNCEMENT")</p>
```

Every page that renders the component, including through a layout, is rendered on every
publish. If external state decides whether a child component appears, mark the parent.

`AlwaysRender` only re-renders HTML; it does not refresh data a service has cached, and
content reloads do not recreate singletons. Build shared derived data with
`AddContentSource` instead. During development, restart `dotnet watch` when external data
changes.

## Reset and inspect

```pwsh
# Render every page once; later publishes reuse the results.
dotnet publish -p:KijiForce=true

# Show per-file details. --tl:off is needed because the default terminal display hides them.
dotnet publish -p:KijiVerbose=true --tl:off

# Remove build output and the cache.
dotnet clean
```

Kiji keeps its publish cache in `.kiji/cache` and development output in `.kiji/dev-site`.
Saving the cache between CI runs often does not pay off, because transferring it takes time
and code or package changes render every page anyway. Before relying on a CI cache,
compare total CI time with and without it.
