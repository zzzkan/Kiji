---
title: Kiji.Sitemaps namespace
navTitle: Kiji.Sitemaps
description: Sitemap registration.
order: 150
section: API reference
---

## `SitemapHostBuilderExtensions`

`AddSitemap(string path = "sitemap.xml", IEnumerable<string>? excludedPaths = null)` lists
every generated page except `404.html`. Each excluded path removes only the page with that
exact path: `preview/` removes `/preview/` but not `/preview/draft/`.

```csharp
builder.AddSitemap(excludedPaths: ["preview/", "internal/status/"]);
```
