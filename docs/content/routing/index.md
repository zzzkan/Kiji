---
title: Routing
description: Generate fixed, parameterized, and not-found pages from Razor routes.
order: 30
---

Kiji uses Razor `@page` routes to decide where generated pages belong. Fixed routes are
discovered from components; parameterized routes also need the set of values to expand.

## Fixed routes

Kiji automatically discovers `@page` routes on public, non-abstract components in the
entry assembly. No page registration is needed for a fixed route:

```razor
@page "/about/"

<h1>About</h1>
```

This generates `about/index.html`, served as `/about/`. A route without a trailing slash,
such as `@page "/about"`, generates the same file, so link to `/about/` with the slash.
The `/` route generates the root `index.html`. Components without `@page` remain ordinary
reusable components.

Pages in another assembly, such as a Razor class library, are included only when you add
that assembly. To leave out one of its pages, register an empty parameter sequence:

```csharp
app.AddPageAssembly(typeof(SharedPage).Assembly);
app.AddPageParameters<UnwantedPage>(_ => []);
```

## Parameterized routes

Use `AddPageParameters<TPage>` to supply the values of a component's parameterized route:

```razor
@page "/posts/{Slug}/"

<h1>@Slug</h1>

@code {
    [Parameter] public string Slug { get; set; } = string.Empty;
}
```

The factory supplies one parameter object for every page:

```csharp
app.AddPageParameters<PostPage>(_ =>
[
    new { Slug = "first-post" },
    new { Slug = "second-post" },
]);
```

These values generate `posts/first-post/index.html` and
`posts/second-post/index.html`. You may return anonymous objects or dictionaries with
string keys and object values. Every parameterized route needs parameters; return an
empty sequence to generate no pages. Multiple calls for the same component concatenate.

Parameter names are matched case-insensitively to public writable `[Parameter]`
properties. Values keep their .NET types; Kiji does not convert a string into another
component parameter type. Route values must be nonempty and fit in one URL segment.

### Several routes on one component

A component can combine fixed and parameterized routes, for example a blog index with
numbered pages:

```razor
@page "/blog/"
@page "/blog/page/{Page}/"

@code {
    [Parameter] public int Page { get; set; } = 1;
}
```

```csharp
app.AddPageParameters<BlogIndex>(_ => [new { Page = 2 }, new { Page = 3 }]);
```

This generates `blog/index.html` with the default `Page` value, plus
`blog/page/2/index.html` and `blog/page/3/index.html`. With several parameterized routes,
each parameter set is rendered at all of them and must supply all of their values.

## Passing values that are not in the URL

Each object returned by `AddPageParameters` is one page's complete parameter set. Properties
that match route placeholders build the URL; other properties are passed only to the
component. This is useful for looking up content without putting its internal key in the
URL:

```csharp
app.AddPageParameters<PostPage>(services => services
    .GetRequiredService<ContentDictionary<Post>>()
    .Select(post => new
    {
        post.Value.Slug,
        ContentKey = post.Key,
    }));
```

```razor
@code {
    [Parameter] public string Slug { get; set; } = string.Empty;
    [Parameter] public string ContentKey { get; set; } = string.Empty;
}
```

Every supplied name must match a component parameter, even when it is not part of the
route. Omitted parameters keep their component defaults; Kiji does not enforce
`EditorRequired`.

Fixed routes can receive parameters too:

```csharp
app.AddPageParameters<AboutPage>(_ => [new { Title = "About this site" }]);
```

This renders `/about/` with the supplied values instead of the defaults.

## Route limits and validation

Static generation must know every output path in advance. Kiji therefore supports fixed
segments and simple parameter segments, but rejects:

- Optional parameters such as `{id?}`
- Constraints such as `{id:int}`
- Catch-all parameters such as `{*path}` or `{**path}`
- Composite segments such as `post-{id}`

Kiji also reports an error before rendering any page when a required route value is missing
or empty, a value has the wrong component parameter type, a supplied property has no
matching `[Parameter]`, or two pages resolve to the same output path.

## Not-found pages

Register a routed component with `AddNotFoundPage<TPage>()`:

```csharp
app.AddNotFoundPage<NotFoundPage>();
```

```razor
@page "/not-found/"

<h1>Page not found</h1>
```

Kiji writes it as `404.html` at the output root. The component still needs one `@page`
route, but do not also register it with `AddPageParameters<TPage>`. A not-found page from
another assembly also needs `AddPageAssembly`.

See the [API reference](../api-reference/#pages) for registration
timing and the full method contracts.
