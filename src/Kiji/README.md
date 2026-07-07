# Kiji

Kiji is a static site generator framework for .NET. Pages are Razor components
rendered to static HTML via `HtmlRenderer`, assembled with a minimal-API style
builder, with a built-in on-demand dev server.

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
app.MapPages<Root>();          // discovers all @page components in Root's assembly
app.MapNotFound<NotFound>();   // rendered as 404.html

return await app.RunAsync();   // build (default) | clean | serve | preview
```

Run `dotnet run` to build the site into `dist/wwwroot`, or `dotnet run serve`
for the live-reloading dev server.

## Related packages

- `Kiji.Markdown` — markdown content sources with YAML front matter
- `Kiji.Images` — responsive WebP image optimization
- `Kiji.Feeds` — RSS feed generation (`app.MapFeed(...)`)
- `Kiji.Sitemaps` — sitemap generation (`app.MapSitemap()`)
