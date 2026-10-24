---
title: Incremental builds
description: Reuse unchanged pages, control page rendering, and cache builds in CI.
order: 60
---

Keep using `dotnet publish`. The first publish renders every page; later publishes
reuse eligible HTML when its code, settings, parameters, and inputs are unchanged.
Every successful publish produces a complete static site.

## What changes cause work?

| Change | Work |
| --- | --- |
| No relevant change | Reuse eligible pages. |
| Markdown changes | Render pages reading that item or its collection. |
| Content is added, removed, or reordered | Reevaluate collection readers and routes; remove obsolete outputs. |
| Page parameters change | Render affected pages. |
| A local image changes | Render its readers and update image variants. |
| A static asset changes | Update the asset and pages using `Assets` or the default `ImportMap`. |
| Code, referenced assemblies (including the framework), or site settings change | Render all pages. |
| A page renders an `AlwaysRender` component or layout | Render that page on every publish. |
| `KijiForce=true` | Render all pages. |

Static assets are checked and feeds, sitemaps, and custom artifacts run on every publish.

## Understand page dependencies

`Posts[ContentKey]` depends on one item. Enumeration, `Count`, `Keys`, `Values`, and
missing lookups depend on the entire collection, including its order and contents.
Filtering after `Values` does not narrow that dependency.

Reads in layouts and child components count too: a post page can reuse HTML when
another post changes, but a layout listing every post makes that page depend on
the whole collection.

## Pass stable page parameters

Pass a content key or another simple identifier through `AddPageParameters`, then look up
the content during rendering. Entire models and arrays can be valid component
parameters while preventing HTML reuse. See [Routing](../routing/#passing-values-that-are-not-in-the-url)
and the [supported parameter types](../api-reference/#pages).

## Render pages that use external data

Reads through `ContentDictionary<T>` are tracked automatically. For custom data or a
shared index, [use a content source](../api-reference/#content-sources-and-contentdictionary)
to load and share values; supply item digests to make their readers eligible for HTML reuse.

Kiji cannot observe arbitrary file, HTTP, environment, clock, or service-state reads
outside that pipeline. When those values affect HTML, mark the component that uses them:

```razor
@attribute [AlwaysRender]

<p>@Environment.GetEnvironmentVariable("SITE_ANNOUNCEMENT")</p>
```

Import `Kiji` in `_Imports.razor`. Any page rendering the marked component is regenerated
in full on every publish. This also works in layouts and child components. If external
state controls whether a child appears, mark the parent reading that state.

`AlwaysRender` controls HTML reuse. It does not reload a content dictionary or rebuild
a singleton's cached results. Use `UseContentSource` for shared derived data that must
be rebuilt on content reload; rendering again cannot fix an outdated value in a service.

For a one-time refresh of all pages, use `dotnet publish -p:KijiForce=true`.
Later publishes can reuse those results; `AlwaysRender` pages continue to render.

Neither these rendering controls nor custom content loaders watch external data;
restart the development server when that data changes. Rendering controls also leave
image processing caches intact.

## Cache locations

| Location, relative to the project | Purpose |
| --- | --- |
| `.kiji/cache` | Publish cache; this is the directory to preserve across checkouts. |
| `.kiji/dev-site` | Development output. |
| `dist` | Deployable site. |

In CI, restore the cache before publishing and save it after success. Separate caches
by site and build environment; provide a fallback key so content-only revisions can
reuse earlier output. Changed code, dependencies, or settings may still require a rebuild.
Compare total CI time with and without caching, including transfer time.

Release site builds omit debug symbols and normalize source metadata for portable
reuse. Use Debug for source-level debugging.

## Inspect or reset reuse

```pwsh
# Show per-file details.
dotnet publish -p:KijiVerbose=true --tl:off

# Render every page.
dotnet publish -p:KijiForce=true

# Remove build output and cache.
dotnet clean
```
