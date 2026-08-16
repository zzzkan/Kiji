---
title: Getting started
description: Install the package, declare a site, and run the dev server.
order: 10
---

Kiji is a library, not a CLI tool. Your site is a console app that references it, so you
run the site with `dotnet run` and everything is ordinary C#.

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

var builder = KijiApp.CreateBuilder(args);
builder.Site = new SiteInfo
{
    BaseUrl = new Uri("https://example.com/"),
    Name = "My Site",
};

// The front matter shape is yours; Kiji does not define one.
var posts = builder.AddMarkdownContent<PostFrontMatter>()
    .WithKey(post => post.FileInfo.RelativeDirectoryPath)
    .OrderByDescending(post => post.FrontMatter.CreatedAt);

await using var app = builder.Build();

app.MapDefaultLayout<MainLayout>();
app.MapPages();
app.MapNotFound<NotFoundPage>();

app.MapRoutes<PostPage, MarkdownContent<PostFrontMatter>>(
    posts,
    post => new { Slug = post.FileInfo.RelativeDirectoryPath });

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
| `dotnet run` | Builds the site into `dist/`, incrementally |
| `dotnet run dev` | Dev server with live reload, default port 8080 |
| `dotnet run preview` | Serves `dist/` the way a static host would |
| `dotnet run clean` | Deletes `dist/` and the build cache |

`build` takes `--force` for a full rebuild and `--verbose` for per-file output. `dev` and
`preview` take `--port`.

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
