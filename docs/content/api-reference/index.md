---
title: API reference
description: Types and members of the Kiji package, and the MSBuild property it reads.
order: 100
section: API reference
---

`Program.cs` examples assume `using Kiji;`,
`using Microsoft.Extensions.DependencyInjection;`, and a `builder` from
`StaticSite.CreateBuilder(args)`.

- [`Kiji` namespace](../api-kiji/): the site builder, site and page metadata, content
  collections, and site-wide files
- [`Kiji.Markdown` namespace](../api-markdown/): Markdown content
- [`Kiji.Assets` namespace](../api-assets/): image processing
- [`Kiji.Feeds` namespace](../api-feeds/): RSS feeds
- [`Kiji.Sitemaps` namespace](../api-sitemaps/): sitemaps
- [Blazor APIs](../api-blazor/): ASP.NET Core components and services in static pages
