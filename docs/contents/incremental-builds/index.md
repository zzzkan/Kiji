---
title: Incremental builds
description: Understand what Kiji reuses, declare external inputs, and preserve the publish cache in CI.
order: 60
---

Kiji automatically reuses eligible pages on subsequent publishes. Keep using the same
command from your site project directory:

```pwsh
dotnet publish
```

The first publish renders all pages. Later publishes check code, site configuration,
route parameters, and the inputs observed by each page. Kiji reuses HTML only when it
can establish that the page is unchanged and its cached output is valid. Otherwise,
it renders the page again. Every successful publish produces a complete static site.

## What changes cause work?

| Change                                                             | Expected work                                                                                                  |
| ------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------- |
| No relevant change                                                 | Eligible pages reuse HTML. Static files are checked; site artifacts are generated.                             |
| One Markdown file changes                                          | Pages reading that item or its collection render again. This includes reads from layouts and child components. |
| Content is added, removed, or reordered                            | Collection readers and affected routes are reevaluated. Outputs no longer produced are removed.                |
| A page's parameters change                                         | That page renders again. Unsupported parameter types always require rendering.                                 |
| A referenced local image changes                                   | Pages depending on the image render again; required variants are processed.                                    |
| A file in wwwroot changes                                          | Its output is synchronized. This alone does not require unrelated pages to render.                             |
| A declared page input changes                                      | Pages that read the input render again.                                                                        |
| A site-wide build input or relevant site setting changes           | All pages render again.                                                                                        |
| Compiled site code, a referenced library, or the framework changes | All pages render again.                                                                                        |

RSS feeds, sitemaps, and custom artifacts run on every publish. HTML reuse does not skip
the entire publishing process.

## Understand page dependencies

A successful lookup such as `Posts[ContentKey]` records a dependency on one item.
Enumerating the dictionary or accessing `Count`, `Keys`, or `Values` records a dependency
on the collection. A missing lookup also depends on the collection.

Collection dependencies include item order, membership, and each item's contents. Even
if the number of posts stays the same, editing one post can invalidate a collection
reader. Filtering with LINQ after accessing `Values` does not narrow that dependency.

For example, a page that reads only its own post can reuse HTML when another post
changes. If its shared layout also enumerates all posts to build navigation, the page
depends on the whole collection. A change to any post can then require every page using
that layout to render. This is expected behavior.

## Pass stable page parameters

Prefer passing a content key or another simple identifier through `AddPages`, then
look up the content while rendering the page. See [Routing](../routing/#passing-values-that-are-not-in-the-url).

Passing an entire model or an array can prevent HTML reuse even when the component
accepts it. A string content key avoids that restriction. See the
[AddPages contract](../api-reference/#pages-layouts-and-page-services) for the supported
parameter types.

## Declare inputs outside the built-in content pipeline

Kiji cannot automatically observe arbitrary file, HTTP, environment-variable, or clock
access in your code. Declare values that affect output through the appropriate API.

| Input                                             | API                                                                    | Effect                                                                                                         |
| ------------------------------------------------- | ---------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| A file or directory affecting the whole site      | `app.AddBuildInput(path)`                                              | Changes invalidate all pages; the dev server also watches the declared path. Relative paths use the site root. |
| A site-wide external version or setting           | `app.AddBuildInput(key, value)`                                        | Changes invalidate all pages. Read the current value before registering it for each run.                       |
| A value needed by some pages                      | `app.AddPageInput(key, read)` and injected `PageBuildInputs.Read(key)` | Only readers depend on it. The value is evaluated once per snapshot when needed.                               |
| File bytes consumed while rendering               | Static `PageBuildInputs.ReadFile(path)`                                | Records the bytes actually read for that page.                                                                 |
| Custom content items                              | `UseContentSource` with `ContentEntry<T>`                              | Stable IDs and digests allow item-level tracking.                                                              |
| Output that cannot be described deterministically | Static `PageBuildInputs.DisableCache()`                                | The current page renders on each publish.                                                                      |

For example, register an environment setting in `Program.cs`, before `RunAsync`:

```csharp
app.AddPageInput("announcement", () =>
    Environment.GetEnvironmentVariable("SITE_ANNOUNCEMENT") ?? "");
```

Read it in the component that displays it:

```razor
@using Kiji
@inject PageBuildInputs Inputs

<p>@Inputs.Read("announcement")</p>
```

Only pages that read this value depend on it. The registration does not poll external
systems or refresh the environment of an already running process.

Read page inputs during rendering. For files, pass `ReadFile` an absolute path under
your site root. See [PageBuildInputs](../api-reference/#content-sources-and-contentdictionary)
for path resolution and reuse constraints.

These page-level APIs do not add arbitrary file watchers. To trigger development reloads
for a local file outside the standard watched inputs, declare its path with
`AddBuildInput`; this also makes it a site-wide dependency for publishing.

For custom content that should support reuse, choose the `UseContentSource` overload
with `ContentEntry<T>`. See the [content source contracts](../api-reference/#content-sources-and-contentdictionary)
for the required IDs and digests.

Markdown projections track their source file. If a projection additionally reads
external data, declare that dependency too. Build cross-item indexes through
`UseContentSource`, rather than making an individual Markdown projection depend on
other items.

## Cache locations

| Location         | Purpose                                                                             |
| ---------------- | ----------------------------------------------------------------------------------- |
| `.kiji/cache`    | Persistent publish cache. Preserve this directory to reuse output across checkouts. |
| `.kiji/dev-site` | Development output, including images created during preview.                        |
| `dist`           | Default deployable output.                                                          |

The cache and development directories are relative to the site root. With a project
in `MySite/` and the default root, the CI cache path is `MySite/.kiji/cache`.

A restored cache does not guarantee reuse: changes to inputs, the SDK, dependencies,
or build settings can require rendering again. Use a cache namespace that separates
sites and incompatible build environments. If the CI cache uses a unique key per revision,
provide a compatible fallback so a content-only commit can find an earlier cache.
Allow a successful new revision to save its updated cache.

Kiji configures Release site builds for reuse across checkouts. They omit debug symbols
and source revision metadata and normalize source paths; use Debug for source-level
debugging. These settings apply to both build and publish. Referenced projects retain
their own build settings.

## Inspect or reset reuse

Use these commands from the site project directory:

```pwsh
# Include per-file output details. Disable the .NET Terminal Logger so it shows them.
dotnet publish -p:KijiVerbose=true --tl:off

# Render all pages regardless of the previous HTML reuse decision.
dotnet publish -p:KijiForce=true

# Remove build output and Kiji's generated cache, then publish again.
dotnet clean
dotnet publish
```
