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
