---
title: Getting started
description: Install the package, declare a site, and run the dev server.
order: 10
---

Kiji is a library, not a CLI tool. Your site is an ordinary .NET project that references
it, so there are no Kiji commands to learn: `dotnet watch` runs it, `dotnet publish`
generates it.

## Install

```powershell
dotnet new console -o MySite
cd MySite
dotnet add package Kiji
```

Then switch the project to the Razor SDK so you can write pages as `.razor` files:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
```

## Declare the site

`Program.cs` builds the site the way a minimal-API app builds a web host:

```csharp
using Kiji;
using Kiji.Markdown;
using Kiji.Sitemaps;
using Microsoft.Extensions.DependencyInjection;

await using var app = StaticSite.Create(args);
app.Info = new SiteInfo
{
    BaseUrl = new Uri("https://example.com/"),
    Name = "My Site",
};

// The front matter shape is yours; Kiji does not define one.
app.UseMarkdownContent<PostFrontMatter>(key: post => post.FileInfo.Slug);

app.UseDefaultLayout<MainLayout>();
app.AddStaticPages();
app.UseNotFoundPage<NotFoundPage>();

app.AddPages<PostPage>(services => services
    .GetRequiredService<ContentDictionary<MarkdownContent<PostFrontMatter>>>()
    .Select(post => new { Slug = post.Key, ContentKey = post.Key }));

app.AddSitemap();

return await app.RunAsync();
```

Configure `app.Info`, `app.Paths`, and all `Add*` / `Use*` registrations
before calling `RunAsync`. Starting the site makes those settings read-only; later
changes throw. The dev server reloads changed content automatically.

A page is any public component with a route:

```razor
@page "/"

<h1>Hello</h1>
```

`AddStaticPages()` finds parameterless routes in your assembly — writing `@page "/"` makes a
component a fixed page. Register a parameterized page with `AddPages<TPage>`; it does not need assembly discovery.

## Run it

| Command | What it does |
| --- | --- |
| `dotnet watch` | Dev server with live reload and hot reload |
| `dotnet run` | Dev server without hot reload |
| `dotnet publish -c Release -o dist` | Generates the site into `dist/`, incrementally |
| `dotnet clean` | Deletes the build cache |

Publishing takes `-p:KijiForce=true` for a full rebuild and `-p:KijiVerbose=true` for
per-file output. The dev server listens on <http://localhost:8080> unless
`ASPNETCORE_URLS` or a `launchSettings.json` profile says otherwise.

The development server also watches file and directory inputs declared with
`app.AddBuildInput(path)`. Changes invalidate loaded content and reload connected
browsers. Razor and C# changes require `dotnet watch`.

Upload the contents of `dist/` to your static host.

For direct calls to `PublishAsync`, choose an output directory separate from source
and cache directories. Repeated publishes to the same directory reload content. Create
a new `StaticSite` to change output directories or switch between serving and publishing.

## Project layout

| Path | What it is |
| --- | --- |
| `Pages/` | Components with an `@page` route |
| `contents/` | Markdown, one directory per page, images beside them |
| `wwwroot/` | Static assets, copied to the output as-is |
| `dist/` | The generated site |
| `.kiji/` | Build manifest and caches |

Add `dist/` and `.kiji/` to your `.gitignore`.

The site root — what those relative paths resolve against — defaults to your project
directory. Override it with `app.Paths.RootDirectory` if your layout differs.

## Next

Read [Concepts](../concepts/) for how pages, layouts, and content collections fit
together, or jump to [Deployment](../deployment/) if you want to publish first.
