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

var builder = KijiApp.CreateBuilder(args);
builder.Site = new SiteInfo
{
    BaseUrl = new Uri("https://example.com/"),
    Name = "My Site",
};

// The front matter shape is yours; Kiji does not define one.
builder.AddMarkdownContent<PostFrontMatter>(key: post => post.FileInfo.Slug);

await using var app = builder.Build();

app.MapDefaultLayout<MainLayout>();
app.MapPages();
app.MapNotFound<NotFoundPage>();

app.MapRoutes<PostPage>(services => services
    .GetRequiredService<ContentDictionary<MarkdownContent<PostFrontMatter>>>()
    .Select(post => new { Slug = post.Key, ContentKey = post.Key }));

app.MapSitemap();

return await app.RunAsync();
```

A page is any public component with a route:

```razor
@page "/"

<h1>Hello</h1>
```

`MapPages()` finds every one of them in your assembly — writing `@page` is what makes a
component a page, so there is no separate route table to maintain.

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

A publish directory holds the generated site and nothing else — no assemblies, no
`deps.json`. That is Kiji's MSBuild targets replacing the publish output with what your
site generated, so `dotnet publish -o dist` gives you a directory you can upload as-is.

Generating happens inside `KijiApp.RunAsync`, so a site whose entry point never awaits it
will publish nothing.

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
directory. Override it with `builder.Paths.Root` if your layout differs.

## Next

Read [Concepts](../concepts/) for how pages, layouts, and content collections fit
together, or jump to [Deployment](../deployment/) if you want to publish first.
