---
title: Kiji.Feeds namespace
description: RSS feed registration and feed items.
order: 140
section: API reference
---

## `RssFeedHostBuilderExtensions`

`AddRssFeed(Func<IServiceProvider, IEnumerable<FeedItem>> items, string path = "feed.xml")`
writes an RSS 2.0 feed with items in the given order. The channel's title, description,
language, and URLs come from `SiteInfo`.

This example publishes `feed.xml` from the `BlogPost` model in
[Getting started](../getting-started/#add-a-markdown-page), newest first:

```csharp
builder.AddRssFeed(services => services
    .GetRequiredService<ContentCollection<BlogPost>>()
    .OrderByDescending(post => post.Content.FrontMatter.Date)
    .ThenBy(post => post.Slug, StringComparer.Ordinal)
    .Select(post => new FeedItem(post.Content.FrontMatter.Title, post.Content.FrontMatter.Description,
        post.Content.FrontMatter.Date, $"posts/{Uri.EscapeDataString(post.Slug)}/")));
```

Link the feed among the shared head tags so that feed readers can find it:

```razor
<link rel="alternate" type="application/rss+xml" title="@Site.Name"
      href="@Site.ResolveUrl("feed.xml").AbsoluteUri" />
```

## `FeedItem`

`FeedItem(string Title, string Description, DateTimeOffset PublishedAt, string RelativePath)`
describes one item. `RelativePath` is the page path relative to `BaseUrl` without a leading
`/`, such as `posts/hello/`, or `""` for the home page.
