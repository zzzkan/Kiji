# Kiji

Kiji is a static site generator framework for .NET. Pages are Razor components
rendered to static HTML via `HtmlRenderer`, assembled with a minimal-API style
builder. Includes a markdown content pipeline with YAML front matter, responsive
WebP image optimization, RSS feed and sitemap artifacts, and a live-reloading
on-demand dev server.

## Install

```powershell
dotnet add package Kiji
```

## Getting started

```csharp
using Kiji;

var builder = KijiApp.CreateBuilder(args);
builder.Site = new SiteInfo
{
    BaseUrl = new Uri("https://example.com"),
    Name = "My Site",
};

await using var app = builder.Build();

app.MapRoot<Root>(); // the document component wrapping every page
app.MapPages(typeof(Root).Assembly.GetTypes()
    .Where(t => t.Namespace == "MySite.Pages")); // explicit page registration
app.MapNotFound<NotFound>(); // rendered as 404.html

return await app.RunAsync(); // build (default) | dev [--port <n>] | preview [--port <n>]
```

Run `dotnet run` to build the site into `dist`, `dotnet run dev` for the
live-reloading dev server, or `dotnet run preview` to serve the built output.
Add `dist/` and `.kiji/` (the build cache) to your site's `.gitignore`.

Markdown content (`builder.AddMarkdownContent<TFrontMatter>()`), responsive
image optimization, RSS feeds (`app.MapFeed(...)`), and sitemaps
(`app.MapSitemap()`) are all included — see the
[project README](https://github.com/zzzkan/kiji) for the full walkthrough.
