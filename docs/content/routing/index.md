---
title: Routing
description: Generate fixed, parameterized, and not-found pages from Razor routes.
order: 30
---

## Fixed routes

Kiji generates a page for each `@page` route on public, non-abstract components in the
project. No registration is needed:

```razor
@page "/about/"

<h1>About</h1>
```

This generates `about/index.html`, served as `/about/`. `@page "/about"` generates the same
file, so link to `/about/` with the slash. The `/` route generates the root `index.html`.

Pages in another assembly, such as a Razor class library, need `AddPageAssembly`. To leave
out one of its pages, register an empty parameter sequence:

```csharp
builder.AddPageAssembly(typeof(SharedPage).Assembly);
builder.AddPageParameters<UnwantedPage>(_ => []);
```

## Parameterized routes

A route with parameters needs `AddPageParameters<TPage>` to supply one parameter set per
page:

```razor
@page "/posts/{Slug}/"

<h1>@Slug</h1>

@code {
    [Parameter] public string Slug { get; set; } = string.Empty;
}
```

```csharp
builder.AddPageParameters<PostPage>(_ =>
[
    new { Slug = "first-post" },
    new { Slug = "second-post" },
]);
```

This generates `posts/first-post/index.html` and `posts/second-post/index.html`. Each set
is an anonymous object or a dictionary with string keys. Names match `[Parameter]`
properties case-insensitively, and values must already have the parameter's type; Kiji
does not convert strings. Route values must be nonempty and fit in one URL segment.
Return an empty sequence to generate no pages; multiple calls for one component add up.

### Fixed and parameterized routes together

```razor
@page "/blog/"
@page "/blog/page/{Page}/"

@code {
    [Parameter] public int Page { get; set; } = 1;
}
```

```csharp
builder.AddPageParameters<BlogIndex>(_ => [new { Page = 2 }, new { Page = 3 }]);
```

This generates `blog/index.html` with the default `Page`, plus `blog/page/2/index.html`
and `blog/page/3/index.html`. With several parameterized routes, each set is rendered at
all of them and must supply all of their values.

## Passing values that are not in the URL

Properties that match route parameters build the URL; the others are only passed to the
component. Use this to look up content by key without putting the key in the URL:

```csharp
builder.AddPageParameters<PostPage>(services => services
    .GetRequiredService<ContentDictionary<Post>>()
    .Select(post => new { post.Value.Slug, ContentKey = post.Key }));
```

Every supplied name must match a component parameter. Omitted parameters keep their
defaults, and `EditorRequired` is not enforced. A component with only fixed routes can
receive parameter sets too; they replace its default rendering.

## Route limits

Kiji must know every output path in advance, so it rejects optional parameters (`{id?}`),
constraints (`{id:int}`), catch-all parameters (`{*path}`), and composite segments
(`post-{id}`). Missing values, values of the wrong type, unknown names, and duplicate
output paths also fail before any page is rendered.

## Not-found pages

Give the component a route and register it with `UseNotFoundPage<TPage>()`:

```razor
@page "/not-found/"

<h1>Page not found</h1>
```

```csharp
builder.UseNotFoundPage<NotFoundPage>();
```

Kiji writes it as `404.html` at the output root. Do not also register it with
`AddPageParameters<TPage>`.
