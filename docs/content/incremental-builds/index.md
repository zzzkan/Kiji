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
| Markdown body changes | Render pages that receive the item as a parameter, look it up by ID, or render its body. |
| Markdown front matter changes | Also render pages that enumerate the collection, read `Count`, or read the item's `FrontMatter`. |
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

Pass collection items directly through `AddPageParameters`, including related items such
as the previous and next post:

```csharp
builder.AddPageParameters<PostPage>(services =>
{
    var posts = services.GetRequiredService<ContentCollection<Post>>().ToArray();
    return posts.Select((post, index) => new
    {
        post.Slug,
        Post = post,
        Previous = index > 0 ? posts[index - 1] : null,
        Next = index + 1 < posts.Length ? posts[index + 1] : null,
    });
});
```

Each item parameter depends on that item's file, like `Posts[id]`, so the page renders
again when its post or a neighboring post changes, including body-only edits. Resolve
items in the factory on every build; an item retained from an earlier build fails
publishing. Values created in the factory, such as record copies made with `with` or
arrays, disable reuse; see the [reusable parameter types](../api-reference/#pages).

Kiji tracks an item through the file it was loaded from. Values assigned to an item or its
front matter afterward, in the factory or during rendering, are not tracked, so a reused
page can show old values. For example, if the factory sets `post.Next`, adding a post
between two existing ones leaves the first post's page linking to the wrong post. Pass
related items as parameters, as above, or mark a component that reads such values with
[`AlwaysRender`](#render-pages-that-use-external-data).

Enumerating a collection, reading `Count`, or looking up a missing ID depends on the whole
collection, even if you filter afterward. Reads in layouts and child components count too.
These reads ignore body edits unless the projection calls `RenderAsync`.

## Render pages that use external data

Kiji tracks reads through `ContentCollection<T>`, and configuration changes render every
page. Kiji cannot see other reads, such as files, HTTP, environment variables, the clock,
or service state. Mark components that render such values:

```razor
@attribute [AlwaysRender]

<p>@Environment.GetEnvironmentVariable("SITE_ANNOUNCEMENT")</p>
```

Every page that renders the component, including through a layout, is rendered on every
publish. If external state decides whether a child component appears, mark the parent.

`AlwaysRender` only re-renders HTML; it does not refresh data a service has cached, and
content reloads do not recreate singletons. During development, restart `dotnet watch`
when external data changes.

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
