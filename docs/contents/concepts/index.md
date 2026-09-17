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

`AddStaticPages()` scans your assembly for public components carrying a parameterless route and
registers each as a page; parameterized routes are ignored. Use `AddStaticPages(assembly)` for other assemblies. Repeating an assembly registration has no effect, and an assembly with no fixed pages is allowed.

Routes are static or take parameters:

```razor
@page "/blog/{Slug}/"
```

Register parameterized pages directly with [`AddPages`](#routes); no assembly scan is required. The component must declare exactly one parameterized route, which is where
the route set comes from. Catch-all routes, route constraints, optional parameters, and
composite segments are rejected — a static site has to know every URL up front.

Pages are written as `route/index.html`. The dev server redirects `/route` to
`/route/`, preserving the query string, so document-relative images resolve beside
the page. Canonical URLs use the trailing-slash form.

## Layouts

`UseDefaultLayout<TLayout>()` applies a layout to every page. A page opts out or swaps
with `@layout`, and layouts nest by declaring their own. A circular chain throws rather
than hanging.

```razor
@inherits LayoutComponentBase

<header>…</header>
<main>@Body</main>
```

## Content dictionaries

`UseMarkdownContent<TFrontMatter>(...)` and `UseContentSource<T>(...)` declare a
`ContentDictionary<T>`, which materializes lazily. It is keyed: every item has a
non-empty, unique key, supplied where the source is declared.

```csharp
app.UseMarkdownContent<PostFrontMatter>(
    key: post => post.FileInfo.FullName,
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

It is an `IReadOnlyDictionary<string, T>`. Sort explicitly for the display order you want:

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
app.UseMarkdownContent<PostFrontMatter, Post>(
    select: Post.Create,
    key: post => post.Slug,
    configure: options =>
    {
        options.FileFilter = file => !file.FileNameWithoutExtension.StartsWith('_');
        options.AddValidation(post => post.Title.Length > 0, "title is required");
    });
```

`AddValidation` collects every failure and reports them together, each naming its source
file, so bad content is fixed in one pass rather than one rebuild at a time.

The projection runs per item, so each model keeps the source file of the markdown it came
from — which is what lets a keyed lookup stay a single-file dependency. Do not let it
depend on other items.

### Derived data

Tag lists, related posts and archives can share their definitions without sharing their
computed results. Choose where the computation belongs:

| Where | What is shared | When computation runs |
| --- | --- | --- |
| A static method | The definition | Each call |
| A page service (`app.AddPageService<T>()`) | One instance within a page render | Each method call, unless the service caches within that render |
| A derived content dictionary | The computed index across pages | First access, then first access after content invalidation |

A method with only a few dependencies can stay static. Use a page service when constructor
injection makes the dependencies easier to manage. The page, layout and child components
share that instance; another page or another request gets a fresh one. Kiji disposes page
services when rendering finishes, including asynchronous disposal and failed renders.

Register a helper with `app.AddPageService<RelatedPosts>()` and inject it into the
page or layout. For an index shared across pages, register a separate element type with
`UseContentSource<T>`; its factory can resolve the source dictionary from the supplied
service provider. Keep each projection local to its source item and put computations
spanning multiple items in a derived dictionary.

Markdown edits, additions and deletions detected by the dev server invalidate both source
and derived dictionaries. Code hot reload also invalidates them. The next access rebuilds
the index; previously returned objects are not updated in place. A publish invalidates
content before planning its pages.

Reading a derived dictionary during rendering conservatively depends on the whole
content tree, including when the index is already cached.

Page services must be concrete types with public constructors. Duplicate registrations,
replacement of Kiji-managed services, and registration after execution starts are rejected.
Constructor dependencies can include other page services. Missing dependencies and cycles
are errors. Content loaders, route/feed factories and artifacts cannot resolve page services;
use content dictionaries or ordinary methods in those contexts. Service registration and
constructor-shape changes may require restarting `dotnet watch`.

## Routes

`AddPages<TPage>` supplies the parameter sets for a page with a dynamic route template —
one generated page per object. Property names are the page's `[Parameter]` names; those
that also appear in the route template bind the URL, and the rest are passed through.

```csharp
app.AddPages<PostPage>(services => services
    .GetRequiredService<ContentDictionary<Post>>()
    .Select(post => new { Slug = post.Key, ContentKey = post.Key }));
```

This says nothing about content — the factory receives the app's services, and a content
dictionary is just one thing you might resolve from them. Tag pages come from plain LINQ
over the same one:

```csharp
app.AddPages<TagPage>(services => services
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

Resolve content inside a loader, route/feed factory, artifact, or page. It is not
available while declaring the site.

`AddPages<TPage>` evaluates its parameter factory once per registration per snapshot, after
execution paths are settled. Multiple registrations for the same type concatenate their
results; an empty result is allowed. Invalid parameters and duplicate output paths fail
during page planning.

`UseNotFoundPage<TPage>()` registers the component directly, so it does not need an
assembly scan either. It requires one route declaration and replaces any discovered
route for that component with `404.html`, excluded from the sitemap. The same component
cannot also be registered with `AddPages`.

## Artifacts

RSS feeds and sitemaps are opt-in. The feed takes the entries themselves, in the order
they should be read:

```csharp
app.AddRssFeed(services => services
    .GetRequiredService<ContentDictionary<Post>>().Values
    .OrderByDescending(post => post.CreatedAt)
    .Select(post => new FeedItem(post.Title, post.Description, post.CreatedAt, RoutePath: $"blog/{post.Slug}/")));

app.AddSitemap();
```

`RoutePath` is combined with `SiteInfo.BaseUrl`, so it carries a base path automatically —
write it prefix-free, the same as an index page link.

Anything else site-wide implements `ISiteArtifact` and registers with
`app.AddArtifact(...)`. Artifacts run after all pages and see every page's metadata.

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
