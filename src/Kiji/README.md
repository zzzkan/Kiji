# Kiji

A static site generator framework for .NET. Write pages as Razor components, ship static
HTML. Markdown with your own YAML front matter shape, responsive WebP image optimization,
RSS feeds, sitemaps, and a live-reloading dev server are all in this one package.

## Quick start

```powershell
dotnet new install Kiji.Templates
dotnet new kiji -o MySite
cd MySite
dotnet run dev
```

That is a working site on <http://localhost:8080> with live reload. `dotnet run` builds it
into `dist/`, which any static host will serve.

## Or add it to an existing project

```powershell
dotnet add package Kiji
```

```csharp
using Kiji;

var builder = KijiApp.CreateBuilder(args);
builder.Site = new SiteInfo
{
    BaseUrl = new Uri("https://example.com/"),
    Name = "My Site",
};

await using var app = builder.Build();

app.MapDefaultLayout<MainLayout>(); // applied to every page; pages may override via @layout
app.MapPages();                     // every component with an @page route in this assembly
app.MapNotFound<NotFoundPage>();    // rendered as 404.html

return await app.RunAsync(); // build (default) | dev [--port <n>] | preview [--port <n>] | clean
```

A page is any component with a route:

```razor
@page "/"

<h1>Hello</h1>
```

Kiji renders the document shell itself — the doctype, `<html lang>` from
`SiteInfo.Language`, `<head>`, and `<body>`. Pages contribute head content through the
`Kiji.Components.HeadContent` component.

Add `dist/` and `.kiji/` to your `.gitignore`.

## Documentation

<https://zzzkan.github.io/kiji/> — getting started, concepts, markdown and images,
deployment, and performance. The site is itself built with Kiji.
