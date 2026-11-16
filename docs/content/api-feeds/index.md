---
title: Kiji.Feeds namespace
navTitle: Kiji.Feeds
description: RSS feed registration and feed items.
order: 140
section: API reference
---

## `RssFeedHostBuilderExtensions`

`AddRssFeed(Func<IServiceProvider, IEnumerable<FeedItem>> items, string path = "feed.xml")`
writes an RSS 2.0 feed with items in the given order. The channel's title, description,
language, and URLs come from `SiteInfo`.

See [Recipes](../recipes/#rss-feed) for examples.

## `FeedItem`

`FeedItem(string Title, string Description, DateTimeOffset PublishedAt, string RelativePath)`
describes one item. `RelativePath` is the page path relative to `BaseUrl` without a leading
`/`, such as `posts/hello/`, or `""` for the home page.

The constructor throws `ArgumentException` for a path with a leading `/`, a query, or a
fragment, for a path `SiteInfo.ResolveUrl` rejects, such as one with a `..` segment or
surrounding whitespace, and for a title or description containing a character XML cannot
represent, such as a control character other than tab or newline.
