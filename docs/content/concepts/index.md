---
title: Concepts
description: The building blocks of a Kiji site.
order: 20
---

A Kiji site is a .NET project. Razor components define pages, and `Program.cs` creates
the site with `StaticSite.Create`, registers content and settings, and calls `RunAsync`.

## Project structure

```text
MySite/
├── Layout/           shared layouts and navigation
├── Components/       reusable components
├── Pages/            components with @page routes
├── Models/           content models
├── content/          Markdown and its local images
├── wwwroot/          shared CSS, JavaScript, fonts, and images
├── _Imports.razor    @using directives shared by all components
├── MySite.csproj     Razor SDK project with a Kiji package reference
├── Program.cs        site metadata and registrations
├── .kiji/            build cache and development output
└── dist/             published site
```

`content/` is the default Markdown directory and `wwwroot/` holds static assets; organize
components as you prefer. Add `.kiji/` and `dist/` to `.gitignore`.

## Develop and publish

| Command          | Result                                                            |
| ---------------- | ----------------------------------------------------------------- |
| `dotnet watch`   | Serve a preview at <http://localhost:8080> that reloads on changes. |
| `dotnet publish` | Write the static site to `dist/`, reusing unchanged pages.        |
| `dotnet clean`   | Remove build output and the `.kiji/` cache.                       |

`Program.cs` is read only at startup: after changing it, press `Ctrl+R` in the
`dotnet watch` terminal to restart. To use another address, for example when port 8080
is in use:

```pwsh
dotnet watch -- --urls http://127.0.0.1:5000
```

## Pages and routes

Each component with an `@page` route becomes a page: `/about/` becomes `about/index.html`.
Parameterized routes such as `/posts/{Slug}/` get their values from
`AddPageParameters<TPage>`. See [Routing](../routing/).

## Layouts and the document head

`UseDefaultLayout<TLayout>()` sets the layout for pages without their own `@layout`.

Kiji writes `<html>`, `<head>`, and `<body>`. Everything inside `<head>`, including
`<meta charset>`, comes from Blazor's `PageTitle` and `HeadContent`. Separate
`HeadContent` blocks replace each other instead of combining, so keep shared and
page-specific tags in one, for example through a shared component as in
[Getting started](../getting-started/#add-a-layout-and-css).

## Content

Pages inject `ContentDictionary<T>` to list content or look up an item by key. Markdown
provides typed front matter and an HTML body, and images beside it become responsive
images. Content creates no pages by itself: page parameters choose which items get pages.
See [Markdown and images](../markdown/).

## Static assets

Keep shared files in `wwwroot/` and link to them through `Assets`, which adds the
deployment base path and, where available, a fingerprint that changes with the file:

```razor
<link rel="stylesheet" href="@Assets["css/app.css"]" />
<script src="@Assets["js/app.js"]" defer></script>
```

Referenced Razor class libraries supply their assets under `_content/{PackageId}/`.

### Component styles

Place `Pages/Home.razor.css` beside `Pages/Home.razor` to scope styles to that component.
Link the generated bundle among the shared head tags, replacing `MySite` with the
project's assembly name. The bundle also includes referenced libraries' scoped styles.

```razor
<link rel="stylesheet" href="@Assets["MySite.styles.css"]" />
```

To style child components or Markdown HTML, use `::deep`, such as `.doc-content ::deep h2`.
See [Blazor CSS isolation](https://learn.microsoft.com/aspnet/core/blazor/components/css-isolation).

## Generated files

`AddRssFeed`, `AddSitemap`, and `AddArtifact` write feeds, sitemaps, and other site-wide
files. See [Generated files](../api-reference/#generated-files).

## Static output

The output has no Blazor runtime: event handlers such as `@onclick` and
`OnAfterRenderAsync` never run. Add browser behavior with JavaScript in `wwwroot/`. See
[Unsupported Blazor features](../api-reference/#unsupported-blazor-features).
