---
title: Concepts
description: Pages, the document shell, layouts, content dictionaries, and artifacts.
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

Parameterized routes need their values supplied by [`MapRoutes`](#routes), which is where
the route set comes from. Catch-all routes, route constraints, optional parameters, and
composite segments are rejected — a static site has to know every URL up front.

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

## Content dictionaries

`AddMarkdownContent<TFrontMatter>(...)` and `AddContentSource<T>(...)` declare a
`ContentDictionary<T>`, which materializes lazily. It is keyed: every item has a
non-empty, unique key, supplied where the source is declared.

```csharp
builder.AddMarkdownContent<PostFrontMatter>(
    key: post => post.FileInfo.Slug,
    configure: options => options.Directory = "posts");
```

There is no handle to pass around. **A dictionary is identified by its element type**, and
it is resolved from services — by a component through injection, or by a route or feed
factory through the provider it is handed.

```razor
@inject ContentDictionary<MarkdownContent<PostFrontMatter>> Posts
```

Two dictionaries therefore need two element types. Declaring the same one twice is an error
rather than a silent overwrite.

It is an `IReadOnlyDictionary<string, T>`, and has **no declared order** —
enumeration yields entries by ascending key, and pages sort as they see fit at render
time. Ordering is a view concern, and a fixed enumeration order is what keeps generated
output byte-identical across rebuilds.

```razor
@foreach (var (slug, post) in Posts.OrderByDescending(entry => entry.Value.FrontMatter.CreatedAt))
{
    <a href="@Site.Path($"blog/{slug}/")">@post.FrontMatter.Title</a>
}
```

**Look items up through the indexer, not with a search.** `Posts[key]` is a dictionary
lookup, and it tells the incremental build that the page depends on that one source file.
`Posts.Values.First(p => ...)` enumerates instead, which makes the page depend on the
whole content set — so editing one post would re-render all of them.

### Your own model

Pass a projection to work with your own type instead of `MarkdownContent<T>`. That
projection is also where content that does not belong gets rejected:

```csharp
builder.AddMarkdownContent<PostFrontMatter, Post>(
    select: Post.Create,
    key: post => post.Slug,
    configure: options =>
    {
        options.Where = file => !file.FileNameWithoutExtension.StartsWith('_');
        options.Validate(post => post.Title.Length > 0, "title is required");
    });
```

`Validate` collects every failure and reports them together, each naming its source
file, so bad content is fixed in one pass rather than one rebuild at a time.

The projection runs per item, so each model keeps the source file of the markdown it came
from — which is what lets a keyed lookup stay a single-file dependency. Do not let it
depend on other items.

### Derived data

Tag lists, related posts, archives — anything computed from *all* the content — needs no
special API. There are three places to put it:

| Where | Recomputed | Use it when |
| --- | --- | --- |
| In the page (inject the dictionary and enumerate) | per page | the default; display-time computation like related posts |
| A scoped service (`builder.Services.AddScoped<T>`) | per page | several pages share the computation |
| A derived dictionary (below) | once per build | the computation is expensive |

The first is the default for a reason: a page that enumerates a dictionary records a
dependency on the whole content set by doing so, which is exactly right — a page showing
related posts really does change when any post changes. Nothing to reason about.

A derived dictionary is an ordinary content source whose loader resolves another one:

```csharp
builder.AddContentSource<Tag>(
    services => Tag.CollectFrom(services.GetRequiredService<ContentDictionary<Post>>()),
    key: tag => tag.Slug);
```

> Register derived data as **scoped**, never as a singleton. The dev server rebuilds
> content when it changes but leaves your singletons alone, so a singleton would
> keep serving what it computed at startup.

## Routes

`MapRoutes<TPage>` supplies the parameter sets for a page with a dynamic route template —
one generated page per object. Property names are the page's `[Parameter]` names; those
that also appear in the route template bind the URL, and the rest are passed through.

```csharp
app.MapRoutes<PostPage>(services => services
    .GetRequiredService<ContentDictionary<Post>>()
    .Select(post => new { Slug = post.Key, ContentKey = post.Key }));
```

This says nothing about content — the factory receives the app's services, and a content
dictionary is just one thing you might resolve from them. Tag pages come from plain LINQ
over the same one:

```csharp
app.MapRoutes<TagPage>(services => services
    .GetRequiredService<ContentDictionary<Post>>().Values
    .SelectMany(post => post.Tags).Distinct()
    .Select(tag => new { TagSlug = tag }));
```

Note that `Slug` and `ContentKey` above are **different things** that happen to hold the
same value: `Slug` is the page's route segment, `ContentKey` is how the page finds itself
in the dictionary. Declaring both keeps them free to diverge.

```razor
@page "/blog/{Slug}/"
@code {
    [Parameter] public string Slug { get; set; } = string.Empty;
    [Parameter] public string ContentKey { get; set; } = string.Empty;

    protected override void OnParametersSet() => _post = Posts[ContentKey];
}
```

**The factory is the earliest place content exists.** That is not a convention to
remember — there is deliberately no way to get a content dictionary, or a service provider, out of
the app while you are declaring the site. Loading content resolves the site's output
paths, and the command being run is what decides those (`dev` writes somewhere different
than `build`), so content becomes reachable only once a command has started.

## Artifacts

RSS feeds and sitemaps are opt-in. The feed takes the entries themselves, in the order
they should be read:

```csharp
app.MapFeed(services => services
    .GetRequiredService<ContentDictionary<Post>>().Values
    .OrderByDescending(post => post.CreatedAt)
    .Select(post => new FeedItem(post.Title, post.Description, post.CreatedAt, RoutePath: $"blog/{post.Slug}/")));

app.MapSitemap();
```

`RoutePath` is combined with `SiteInfo.BaseUrl`, so it carries a base path automatically —
write it prefix-free, the same as an index page link.

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
