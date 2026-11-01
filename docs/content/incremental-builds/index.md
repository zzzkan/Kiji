---
title: Incremental builds
description: Reuse unchanged pages and control page rendering.
order: 60
---

The first `dotnet publish` renders every page. Later publishes reuse a page's HTML when
nothing it depends on has changed, and always produce a complete site.

## What changes cause work?

| Change | Pages rendered again |
| --- | --- |
| A Markdown file changes | Pages that receive the item, look it up by ID, or render its body. Front matter changes also render pages that enumerate the collection. |
| Content is added, removed, or reordered | Pages that enumerate the collection. Pages are added or removed. |
| Page parameters change | The affected pages. |
| A local image changes | Pages that use it. |
| A static asset is added, removed, or changed | Every page that uses `Assets` or `ImportMap`. |
| Code, packages, `SiteInfo`, the environment name, or [configuration](../api-reference/#configuration) changes | All pages. |

Pages that contain an `AlwaysRender` component or an error boundary render on every
publish, so an error boundary in a shared layout affects every page that uses it. Feeds,
sitemaps, and custom artifacts are regenerated on every publish.

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

Each page renders again when its post or a neighboring post changes. Values created in the
factory, such as arrays or record copies made with `with`, make the page render on every
publish; see the [reusable parameter types](../api-reference/#page-parameters).

Kiji does not track values assigned to an item after loading, so a reused page can show
old values. For example, if the factory sets `post.Next`, adding a post between two others
leaves the first post's page linking to the wrong post. Pass related items as parameters,
as above.

## Render pages that use external data

Kiji tracks reads through `ContentCollection<T>`, but not files, HTTP, environment
variables, the clock, or service state. Mark components that render such values:

```razor
@attribute [AlwaysRender]

<p>@Environment.GetEnvironmentVariable("SITE_ANNOUNCEMENT")</p>
```

Every page that renders the component, including through a layout, is rendered on every
publish. If external state decides whether a child component appears, mark the parent.
`AlwaysRender` does not refresh data that a service has cached. While serving, restart
`dotnet watch` when external data changes.

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

The publish cache is in `.kiji/cache`. Restoring it in CI often costs more time than it
saves, because code and package changes render every page anyway; measure before relying
on it.
