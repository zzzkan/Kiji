---
title: Concepts
description: The building blocks of a Kiji site, and when Kiji fits.
order: 20
section: Introduction
---

A Kiji site is a .NET project. Razor components define pages, and `Program.cs` creates a
builder with `StaticSite.CreateBuilder`, registers content and settings, builds the site,
and calls `RunAsync`. Kiji renders each page to HTML with Blazor's
[`HtmlRenderer`](https://learn.microsoft.com/aspnet/core/blazor/components/render-components-outside-of-aspnetcore).

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

`content/` is the default Markdown directory and `wwwroot/` holds static assets. Files in
`content/` are never compiled; organize components as you prefer outside it. Add `.kiji/`
and `dist/` to `.gitignore`.

## Develop and publish

| Command          | Result                                                            |
| ---------------- | ----------------------------------------------------------------- |
| `dotnet watch`   | Serve a preview at <http://localhost:8080> that reloads on changes. |
| `dotnet publish -o dist` | Write the static site to `dist/`, reusing unchanged pages. |
| `dotnet clean`   | Remove build output and the `.kiji/` cache.                       |

Changes to `Program.cs` apply only after a restart: press `Ctrl+R` in the `dotnet watch`
terminal. To use another address, for example when port 8080
is in use:

```powershell
dotnet watch -- --urls http://127.0.0.1:5000
```

## Pages and routes

Each component with an `@page` route becomes a page; `/about/` is written to `about/index.html`.
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

Pages inject `ContentCollection<T>` to list content or look up an item by ID. Markdown
provides typed front matter and an HTML body, and images beside a Markdown file become
responsive images. Content creates no pages by itself: page parameters choose which items get pages.
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

[`AddRssFeed`](../api-feeds/#rssfeedhostbuilderextensions),
[`AddSitemap`](../api-sitemaps/#sitemaphostbuilderextensions), and
[`AddArtifact`](../api-kiji/#staticsitehostbuilderextensions) write feeds, sitemaps,
and other site-wide files. Only `dotnet publish` writes these files; the development
server returns 404 for them. See [Recipes](../recipes/#rss-feed).

## Static output

The output has no Blazor runtime, so event handlers such as `@onclick` and methods such as
`OnAfterRenderAsync` never run. Add browser behavior with JavaScript in `wwwroot/`. See
[Blazor APIs](../api-blazor/).

## When Kiji fits

- Blogs, documentation, and project or landing pages whose content is known when you
  publish.
- Teams that already use C# and Razor and want to share models, components, and Razor
  class libraries with the site.
- Sites hosted as static files, for example on GitHub Pages or a CDN.

## When to choose something else

- **Pages that depend on the request**, such as sign-in, per-user content, or form
  handling on the server. Use ASP.NET Core, for example a Blazor Web App.
- **Applications driven by Razor event handlers.** Kiji's output has no Blazor runtime,
  so `@onclick` and similar handlers never run. Add browser behavior with JavaScript, or
  use Blazor WebAssembly for an application that runs in the browser.
- **Routes that are not known in advance.** Kiji writes one file per page, so optional,
  constrained, and catch-all route parameters are rejected. See
  [Route limits](../routing/#route-limits).
- **Projects without .NET.** Building a site needs the .NET 10 SDK or later.

Kiji is in preview, so its APIs can still change between releases.

## Compared with Blazor hosting models

Kiji uses Blazor's components and renderer, but not its hosting models:

|                               | Kiji            | Blazor Web App (static server rendering)  | Blazor WebAssembly  |
| ----------------------------- | --------------- | ----------------------------------------- | ------------------- |
| When HTML is rendered         | At publish      | On each request                           | In the browser      |
| What serves the site          | Any static host | An ASP.NET Core server                    | Any static host     |
| .NET runtime in the browser   | No              | No                                        | Yes                 |
| Event handlers like `@onclick` | Never run      | Run only with an interactive render mode  | Run                 |
