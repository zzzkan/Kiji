---
title: Feeds and sitemaps
description: Publish RSS feeds, a sitemap, robots.txt, and other generated files.
order: 47
section: Guides
---

Feeds, sitemaps, and other site-wide files are written after all pages. Only
`dotnet publish` writes them; the development server returns 404 for them. They are
regenerated on every publish.

## RSS feed

`AddRssFeed` writes an RSS 2.0 feed from the items you return, in the order you return
them. This example lists the `BlogPost` model from
[Getting started](../getting-started/#add-a-markdown-page), newest first:

```csharp
using Kiji.Feeds;

builder.AddRssFeed(services => services
    .GetRequiredService<ContentCollection<BlogPost>>()
    .OrderByDescending(post => post.Content.FrontMatter.Date)
    .ThenBy(post => post.Slug, StringComparer.Ordinal)
    .Take(20)
    .Select(post => new FeedItem(post.Content.FrontMatter.Title, post.Content.FrontMatter.Description,
        post.Content.FrontMatter.Date, $"posts/{Uri.EscapeDataString(post.Slug)}/")));
```

This writes `feed.xml` at the site root. The channel's title, description, and language
come from `SiteInfo`. Each item's link, which is also its `guid`, is the absolute URL of
its `RelativePath` under `BaseUrl`, so set `BaseUrl` to the public URL before publishing.
`Take` limits the feed to the latest posts.

Link the feed among the shared head tags so that feed readers can find it:

```razor
<link rel="alternate" type="application/rss+xml" title="@Site.Name"
      href="@Site.ResolveUrl("feed.xml").AbsoluteUri" />
```

### Several feeds

Pass a path to write another feed, for example one per tag:

```csharp
builder.AddRssFeed(services => services
    .GetRequiredService<ContentCollection<BlogPost>>()
    .Where(post => post.Content.FrontMatter.Tags.Contains("dotnet"))
    .OrderByDescending(post => post.Content.FrontMatter.Date)
    .Select(post => new FeedItem(post.Content.FrontMatter.Title, post.Content.FrontMatter.Description,
        post.Content.FrontMatter.Date, $"posts/{Uri.EscapeDataString(post.Slug)}/")),
    path: "tags/dotnet/feed.xml");
```

See [`AddRssFeed`](../api-feeds/#rssfeedhostbuilderextensions) and
[`FeedItem`](../api-feeds/#feeditem).

## Sitemap

`AddSitemap` writes `sitemap.xml` with the absolute URL of every generated page except
`404.html`:

```csharp
using Kiji.Sitemaps;

builder.AddSitemap();
```

To leave pages out, pass their paths relative to `BaseUrl`. Each path removes only the
page with that exact path:

```csharp
builder.AddSitemap(excludedPaths: ["preview/", "internal/status/"]);
```

See [`AddSitemap`](../api-sitemaps/#sitemaphostbuilderextensions).

## robots.txt

A fixed `robots.txt` can live in `wwwroot/`. To include the sitemap's absolute URL, write
the file with `AddArtifact`:

```csharp
using System.Text;

builder.AddArtifact("robots.txt", async (stream, context, cancellationToken) =>
{
    var text = $"User-agent: *\nAllow: /\nSitemap: {context.Site.ResolveUrl("sitemap.xml").AbsoluteUri}\n";
    await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
});
```

Crawlers read `robots.txt` only at the root of a host, so this applies when the site is
not published under a subpath.

## Other generated files

`AddArtifact` writes any file from the site's pages and services. The writer receives a
[`SiteOutputContext`](../api-kiji/#siteoutputcontext) with the site metadata, the list of
generated pages, and the services. See [Recipes](../recipes/#json-index-of-posts) for a
JSON example.
