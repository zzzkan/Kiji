# Kiji.Sitemaps

Sitemap generation for the [Kiji](https://github.com/zzzkan/kiji) static site
generator. The sitemap is registered as a site artifact and written at build
time, after all pages are rendered.

## Install

```powershell
dotnet add package Kiji.Sitemaps
```

## Getting started

```csharp
using Kiji.Sitemaps;

app.MapSitemap();

// Or with a custom path:
app.MapSitemap("seo/sitemap.xml");
```

Every generated page is listed except those marked `ExcludeFromSitemap`
(e.g. the not-found page). URLs are sorted for deterministic output.
