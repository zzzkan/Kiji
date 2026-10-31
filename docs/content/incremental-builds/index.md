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
| Code, packages, the .NET runtime, `SiteInfo`, the environment name, or [configuration](../api-reference/#configuration) change | Render all pages. |
| A page uses an `AlwaysRender` component | Render that page on every publish. |
| A page renders an `ErrorBoundaryBase` component | Render that page on every publish, even when healthy. |

Feeds, sitemaps, and custom artifacts are regenerated on every publish.

A boundary in a shared layout makes every page using that layout render on every publish.

Reusing HTML does not replay its log messages, and logs never fail a publish; see
[Logging](../api-reference/#logging).

## Page dependencies

Pass collection items directly through `AddPageParameters`:

```csharp
builder.AddPageParameters<PostPage>(services => services
    .GetRequiredService<ContentCollection<Post>>()
    .Select(post => new { post.Slug, Post = post }));
```

Each item parameter depends on that item, just like `Posts[id]`. Changing a `Previous`
or `Next` item changes the parameter identity and renders the page again. Resolve items
in the factory for each build; retaining an item from an earlier snapshot fails publishing.
Objects created in the factory, record copies made with `with`, and arrays disable reuse;
see the [reusable parameter types](../api-reference/#pages).

Enumerating a collection, reading `Count`, or looking up a missing ID depends on the whole
collection, even if you filter afterward. Reads in layouts and child components count too.

## Render pages that use external data

Kiji tracks reads through `ContentCollection<T>`. Compute derived data while rendering,
or share the calculation through `builder.Services.AddScoped<T>()`. For per-item data,
use Markdown files with front matter and no body. For small datasets, add a project-local
JSON file with `builder.Configuration.AddJsonFile`; configuration changes render every page.
For data fetched externally, use `[AlwaysRender]` on components that display it.

Kiji cannot see other reads, such as files, HTTP, environment variables, the clock, or
service state. Mark components that render such values:

```razor
@attribute [AlwaysRender]

<p>@Environment.GetEnvironmentVariable("SITE_ANNOUNCEMENT")</p>
```

Every page that renders the component, including through a layout, is rendered on every
publish. If external state decides whether a child component appears, mark the parent.

`AlwaysRender` only re-renders HTML; it does not refresh data a service has cached, and
content reloads do not recreate singletons. Compute shared derived data during rendering or in scoped services. During development, restart `dotnet watch` when external data
changes.

## Reset and inspect

```pwsh
# Render every page once; later publishes reuse the results.
dotnet publish -p:KijiForce=true

# Show Kiji's summary in a terminal.
dotnet publish -v d

# Remove build output and the cache.
dotnet clean
```

For a message per page, set `Kiji.Build` to `Debug` in the
[Logging](../api-reference/#logging) section. Changing that section keeps the cache.

Kiji keeps its publish cache in `.kiji/cache` and development output in `.kiji/dev-site`.
Saving the cache between CI runs often does not pay off, because transferring it takes time
and code or package changes render every page anyway. Before relying on a CI cache,
compare total CI time with and without it.
