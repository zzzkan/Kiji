---
title: Concepts
description: The building blocks and standard structure of a Kiji site.
order: 20
---

A Kiji site is an ordinary .NET project. Razor components define the pages, `Program.cs`
connects content to those pages, and the standard .NET commands preview or publish the
result.

## Standard project structure

```text
MySite/
├── Components/       layouts and reusable Razor components
├── Pages/            components with @page routes
├── Models/           data models
├── contents/         Markdown and files that belong to content
├── wwwroot/          CSS, JavaScript, fonts, and other static assets
├── Program.cs        site metadata and registrations
└── dist/             generated site after publishing
```

These names are conventions rather than a separate Kiji project format. `contents/` and
`wwwroot/` can be changed through `SitePaths`, and you can organize components however you
prefer.

## The site definition

Every site follows the same small lifecycle:

```csharp
var app = StaticSite.Create(args);

app.Info = new SiteInfo
{
    Name = "My site",
    BaseUrl = new Uri("https://example.com/"),
};

// Use* selects site-wide behavior and content.
// Add* registers pages and generated files.

return await app.RunAsync();
```

Configure the site before `RunAsync`.

## Develop and publish

Kiji uses the standard .NET commands; there is no separate Kiji CLI to learn.

```pwsh
dotnet watch
```

`dotnet watch` starts the development server at <http://localhost:8080>. It renders pages
as you visit them and refreshes the browser when components, content, or static assets
change. This is a preview environment rather than deployable output.

```pwsh
dotnet publish -c Release -o dist
```

`dotnet publish` renders every registered page, copies static assets, and writes the
complete site to `dist/`. The directory contains static files only and can be uploaded to
any static host.
Development and publishing
share the same rendering path, so the pages you preview are the pages Kiji publishes. See
[Deployment](../deployment/) for hosting and deployment sub-paths.

```pwsh
dotnet clean
```

`dotnet clean` removes the project's build output and Kiji's `.kiji/` build cache. The
next publish rebuilds the complete site instead of reusing output from an earlier build.
It does not serve or publish the site.

## Pages and routes

Pages are Razor components with `@page` routes. Fixed routes such as `/about/` are found by
`AddStaticPages()`. A parameterized route such as `/posts/{Slug}/` uses `AddPages<TPage>`
to provide one parameter set for every page to generate.

Kiji writes clean URLs as directories containing `index.html`: `/about/` becomes
`about/index.html`. See [Routing](../routing/) for route mapping, parameters, and 404 pages.

## Layouts and the document head

`UseDefaultLayout<TLayout>()` applies a shared Razor layout to pages that do not choose
their own. Layouts render inside the `<body>` of the document Kiji creates.

Use `Kiji.Components.HeadContent` to contribute a title, metadata, or stylesheet links to
the generated `<head>`:

```razor
<HeadContent>
    <title>@Title</title>
    <link rel="stylesheet" href="@(Site.BaseUrl.AbsolutePath + "css/app.css")" />
</HeadContent>
```

## Content and static assets

Content sources expose typed `ContentDictionary<T>` collections. Pages can inject a
collection, list its items, or look up one item by the key supplied through `AddPages`.
Markdown support parses YAML front matter and renders the body to HTML; your mapping code
still decides which content becomes a page and what URL it receives.

Files under `wwwroot/` are copied to the published site without modification. Use it for
shared CSS, JavaScript, fonts, and images. Images referenced relative to a Markdown file
can instead stay beside that file and be processed as responsive page-bundle images. See
[Markdown and images](../markdown/) for both content registration and local images.

## Site-wide output

RSS feeds and sitemaps are optional registrations. `AddRssFeed` creates a feed from the
items you supply, and `AddSitemap` lists the generated pages. `AddArtifact` is available
when a site needs another generated file. Their complete contracts are in the
[API reference](../api-reference/).

## Static output

Kiji produces HTML, CSS, JavaScript, images, and other files that a static host can serve.
There is no Blazor runtime in the output: component event handlers such as `@onclick` do
not remain interactive, and `OnAfterRenderAsync` does not run. Add client-side behavior
with JavaScript in `wwwroot/`.
