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
| `ReusePages = false` or `KijiForce=true` | Render all pages. |

Static assets are checked and feeds, sitemaps, and custom artifacts run on every publish.

## Understand page dependencies

`Posts[ContentKey]` depends on one item. Enumeration, `Count`, `Keys`, `Values`, and
missing lookups depend on the entire collection, including its order and contents.
Filtering after `Values` does not narrow that dependency.

Reads in layouts and child components count too: a post page can reuse HTML when
another post changes, but a layout listing every post makes that page depend on
the whole collection.

## Pass stable page parameters

Pass a content key or another simple identifier through `AddPages`, then look up
the content during rendering. Entire models and arrays can be valid component
parameters while preventing HTML reuse. See [Routing](../routing/#passing-values-that-are-not-in-the-url)
and the [supported parameter types](../api-reference/#pages).

## Render pages that use external data

Kiji cannot observe arbitrary file, HTTP, environment, clock, or service-state reads.
When those values affect HTML, mark the component that uses them:

```razor
@attribute [AlwaysRender]

<p>@Environment.GetEnvironmentVariable("SITE_ANNOUNCEMENT")</p>
```

Import `Kiji` in `_Imports.razor`. `AlwaysRender` works on pages, layouts, and child
components, and is inherited by derived components. Any page that renders the marked
component is regenerated on every publish, including through nested or dynamic children.
A link to that page does not make the linking page regenerate.

Place the attribute on a component that is always reached when external state can affect
the output. For example, if an external flag determines whether to show a child, mark
the component reading that flag; a child that was absent from the last render cannot
disable reuse. Independent reads of another page's generated HTML are not tracked.

To refresh every page, set `ReusePages` before `RunAsync`. Conditions are ordinary C#:

```csharp
if (Environment.GetEnvironmentVariable("REFRESH_EXTERNAL_DATA") == "1")
{
    app.ReusePages = false;
}

await app.RunAsync();
```

`ReusePages` defaults to `true`. Setting it to `false` renders every page in that run
and saves successful results for later publishes. `AlwaysRender` pages remain ineligible
for reuse. You can also force a full render with `dotnet publish -p:KijiForce=true`.
Without one of these controls, an untracked external change can leave cached HTML stale.

These controls govern published HTML reuse. Image processors still need correct cache
identities. They do not watch external files or poll services; restart the development
server when untracked external data changes. Markdown sources retain their built-in
tracking and development watchers. Custom content can continue to use stable
`ContentEntry<T>` IDs and digests; build cross-item indexes through `UseContentSource`.

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
