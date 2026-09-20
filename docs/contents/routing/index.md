---
title: Routing
description: Generate fixed, parameterized, and not-found pages from Razor routes.
order: 30
---

Kiji uses Razor `@page` routes to decide where generated pages belong. Fixed routes are
discovered from components; parameterized routes also need the set of values to expand.

## Fixed routes

`AddStaticPages()` finds public, non-abstract components with fixed routes in the entry
assembly:

```csharp
app.AddStaticPages();
```

```razor
@page "/about/"

<h1>About</h1>
```

This generates `about/index.html`, served as `/about/`. The `/` route generates the root
`index.html`. If pages live in another assembly, pass it to `AddStaticPages(assembly)`.

Parameterized routes are ignored during this scan because Kiji cannot infer which values
you want to publish.

## Parameterized routes

Use `AddPages<TPage>` with a component that declares exactly one parameterized route:

```razor
@page "/posts/{Slug}/"

<h1>@Slug</h1>

@code {
    [Parameter] public string Slug { get; set; } = string.Empty;
}
```

The factory supplies one parameter object for every page:

```csharp
app.AddPages<PostPage>(_ =>
[
    new { Slug = "first-post" },
    new { Slug = "second-post" },
]);
```

These values generate `posts/first-post/index.html` and
`posts/second-post/index.html`. You may return anonymous objects or dictionaries with
string keys and object values.

Parameter names are matched case-insensitively to public writable `[Parameter]`
properties. Values keep their .NET types; Kiji does not convert a string into another
component parameter type. Route values must be nonempty and fit in one URL segment.

## Passing values that are not in the URL

The object returned by `AddPages` is the component's complete parameter set. Properties
that match route placeholders build the URL; other properties are passed only to the
component. This is useful for looking up content without putting its internal key in the
URL:

```csharp
app.AddPages<PostPage>(services => services
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
route.

## Route limits and validation

Static generation must know every output path in advance. Kiji therefore supports fixed
segments and simple parameter segments, but rejects:

- Optional parameters such as `{id?}`
- Constraints such as `{id:int}`
- Catch-all parameters such as `{*path}` or `{**path}`
- Composite segments such as `post-{id}`

Planning also fails when a required value is missing or empty, a value has the wrong
component parameter type, a supplied property has no matching `[Parameter]`, or two pages
resolve to the same output path. These errors are reported before pages are rendered.

## Not-found pages

Register a routed component with `UseNotFoundPage<TPage>()`:

```csharp
app.UseNotFoundPage<NotFoundPage>();
```

```razor
@page "/not-found/"

<h1>Page not found</h1>
```

Kiji writes it as `404.html` at the output root. The component still needs one `@page`
route, but do not also register it with `AddPages`.

See the [API reference](../api-reference/#pages-layouts-and-page-services) for registration
timing and the full method contracts.
