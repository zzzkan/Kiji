---
title: Concepts
description: The building blocks of a Kiji site.
order: 20
---

A Kiji site is a .NET project: Razor components define pages, `Program.cs` connects
them to content, and standard .NET commands preview and publish the result.

## Standard project structure

```text
MySite/
├── Layout/           shared layouts and navigation
├── Components/       reusable components
├── Pages/            components with @page routes
├── Models/           content models
├── content/          Markdown and its local images
├── wwwroot/          shared CSS, JavaScript, fonts, and images
└── Program.cs        site metadata and registrations
```

Organize components as you prefer. Set `UseMarkdownContent`'s `Directory` option
to read Markdown from another location.

## The site definition

```csharp
var app = StaticSite.Create(args);
app.Info = new SiteInfo
{
    Name = "My site",
    BaseUrl = new Uri("https://example.com/"),
};
return await app.RunAsync();
```

Configure the site, page parameters, content, and generated files before `RunAsync`.

## Develop and publish

| Command          | Result                                                                                               |
| ---------------- | ---------------------------------------------------------------------------------------------------- |
| `dotnet watch`   | Preview at <http://localhost:8080>, with browser reloads when components, content, or assets change. |
| `dotnet publish` | Write the complete static site to `dist/`, reusing unchanged pages where possible.                   |
| `dotnet clean`   | Remove build output and the `.kiji/` cache so the next publish rebuilds everything.                  |

Preview and publishing use the same rendering pipeline. Upload `dist/` to a static
host; see [Deployment](../deployment/) for hosting and sub-paths, and
[Incremental builds](../incremental-builds/) for caching.

## Pages and routes

Kiji automatically discovers components with `@page` routes in the entry assembly.
For a route such as `/posts/{Slug}/`, use `AddPageParameters<TPage>` to supply each page's
parameters; missing registration is an error. Fixed routes need no registration:
`/about/` becomes `about/index.html`. Use `AddPageAssembly` to include another assembly.
See [Routing](../routing/) for parameter mapping and 404 pages.

## Layouts and the document head

`UseDefaultLayout<TLayout>()` supplies a shared layout unless a page chooses its own
with `@layout`. Layouts render inside the generated `<body>`.

Use Blazor's `PageTitle` and `HeadContent` for the document head. Keep shared and
page-specific tags in one `HeadContent`, since separate instances replace each other.
See the [head API](../api-reference/#pagetitle-headcontent-and-headoutlet).

## Content and static assets

Pages inject typed `ContentDictionary<T>` collections to list content or look up an
item by key. Markdown supplies YAML front matter and an HTML body; your `AddPageParameters`
mapping chooses which items become pages and their URLs.

Images beside Markdown files become responsive images beside the generated page.
See [Markdown and images](../markdown/) for examples.

### Static assets

Keep shared files in `wwwroot/` and link to them through `Assets`:

```razor
<link rel="stylesheet" href="@Assets["css/app.css"]" />
<script src="@Assets["js/app.js"]" defer></script>
```

These URLs include the deployment base path and change when the file changes.
Referenced Razor libraries also supply assets, usually under `_content/{PackageId}/`.
See the [static asset API](../api-reference/#static-assets) for import maps.

### Component styles

Place `Pages/Home.razor.css` beside `Pages/Home.razor` to scope styles to that
component. Include the generated bundle among the shared tags inside `HeadContent`:

```razor
<link rel="stylesheet" href="@Assets["MySite.styles.css"]" />
```

Replace `MySite` with the project's `PackageId`, which defaults to its assembly name.
The bundle includes referenced libraries' isolated CSS. For child components or
Markdown HTML, use `::deep` from a containing HTML element, such as
`.doc-content ::deep h2`. See [Blazor CSS isolation](https://learn.microsoft.com/aspnet/core/blazor/components/css-isolation).

## Site-wide output

`AddRssFeed` creates a feed from your items, `AddSitemap` lists generated pages,
and `AddArtifact` writes other files. See the [API reference](../api-reference/#generated-files).

## Static output

The site needs no Blazor runtime. `@onclick` does not remain interactive and
`OnAfterRenderAsync` does not run. Add browser behavior with JavaScript in `wwwroot/`.
See [Unsupported Blazor features](../api-reference/#unsupported-blazor-features)
for component limitations and alternatives.
