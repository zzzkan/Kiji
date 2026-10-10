---
title: Incremental builds
description: Reuse unchanged pages, declare external inputs, and cache builds in CI.
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
| Page parameters or a declared page input change | Render affected pages. |
| A local image changes | Render its readers and update image variants. |
| A static asset changes | Update the asset and pages using `Assets` or the default `ImportMap`. |
| Code, framework, site settings, or a site-wide input change | Render all pages. |

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

## Declare inputs outside the built-in content pipeline

Kiji cannot observe arbitrary file, HTTP, environment, or clock access in your code.
Declare data that affects output:

| Input | API |
| --- | --- |
| File or directory used across the site | `AddBuildInput(path)`; also watches it during development. |
| Site-wide setting or external version | `AddBuildInput(key, value)`. |
| Value read by some pages | `AddPageInput(key, read)` and injected `PageBuildInputs.Read(key)`. |
| File read during rendering | `PageBuildInputs.ReadFile(absolutePath)`. |
| Custom content | `UseContentSource` with stable `ContentEntry<T>` IDs and digests. |
| Nondeterministic output | `PageBuildInputs.DisableCache()` during rendering. |

For example, register a setting before `RunAsync`:

```csharp
app.AddPageInput("announcement", () =>
    Environment.GetEnvironmentVariable("SITE_ANNOUNCEMENT") ?? "");
```

Read it in the component that displays it:

```razor
@inject PageBuildInputs Inputs

<p>@Inputs.Read("announcement")</p>
```

Only readers depend on this value. It is evaluated once per snapshot when needed;
registration does not poll external systems or refresh the process environment.
Page-level inputs do not add file watchers. For local files that should trigger
development reloads, also use `AddBuildInput(path)`.

Markdown projections already track their source file. Declare any additional external
data they read, and build cross-item indexes through `UseContentSource`.
See the [input API](../api-reference/#build-inputs-and-controls) for path and digest rules.

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
