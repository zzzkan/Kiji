---
title: Recipes
description: Short solutions for tag pages, pagination, drafts, navigation, feeds, sitemaps, and other generated files.
order: 65
section: Guides
---

These recipes extend the blog from [Getting started](../getting-started/#build-from-scratch).
They assume its `BlogPost` model and a `Tags` list in the front matter:

```csharp
public List<string> Tags { get; init; } = [];
```

## Tag pages

Generate one page per tag by passing each distinct tag as a route value:

```csharp
builder.AddPageParameters<TagPage>(services => services
    .GetRequiredService<ContentCollection<BlogPost>>()
    .SelectMany(post => post.Content.FrontMatter.Tags)
    .Distinct(StringComparer.Ordinal)
    .Select(tag => new { Tag = tag }));
```

The page lists the posts that have its tag:

```razor
@page "/tags/{Tag}/"
@inject SiteInfo Site
@inject ContentCollection<BlogPost> Posts

<h1>@Tag</h1>
<ul>
    @foreach (var post in Posts.Where(post => post.Content.FrontMatter.Tags.Contains(Tag)))
    {
        <li>
            <a href="@Site.ResolveUrl($"posts/{Uri.EscapeDataString(post.Slug)}/").AbsolutePath">@post.Content.FrontMatter.Title</a>
        </li>
    }
</ul>

@code {
    [Parameter] public string Tag { get; set; } = string.Empty;
}
```

A tag must fit in one URL segment, so keep tags free of `/`.

## Pagination

Give the list a fixed route for the first page and a parameterized route for the others:

```razor
@page "/"
@page "/page/{Page}/"
@inject SiteInfo Site
@inject ContentCollection<BlogPost> Posts

<ul>
    @foreach (var post in Ordered.Skip((Page - 1) * PageSize).Take(PageSize))
    {
        <li>
            <a href="@Site.ResolveUrl($"posts/{Uri.EscapeDataString(post.Slug)}/").AbsolutePath">@post.Content.FrontMatter.Title</a>
        </li>
    }
</ul>
<nav>
    @if (Page > 1)
    {
        <a href="@Site.ResolveUrl(Page == 2 ? "" : $"page/{Page - 1}/").AbsolutePath" rel="prev">Newer</a>
    }
    @if (Page * PageSize < Posts.Count)
    {
        <a href="@Site.ResolveUrl($"page/{Page + 1}/").AbsolutePath" rel="next">Older</a>
    }
</nav>

@code {
    public const int PageSize = 10;

    [Parameter] public int Page { get; set; } = 1;

    private IEnumerable<BlogPost> Ordered => Posts
        .OrderByDescending(post => post.Content.FrontMatter.Date)
        .ThenBy(post => post.Slug, StringComparer.Ordinal);
}
```

Register the pages after the first. The fixed route renders with the default `Page`:

```csharp
builder.AddPageParameters<Home>(services =>
{
    var posts = services.GetRequiredService<ContentCollection<BlogPost>>().Count;
    var pages = (posts + Home.PageSize - 1) / Home.PageSize;
    return Enumerable.Range(2, Math.Max(0, pages - 1)).Select(page => new { Page = page });
});
```

## Drafts

Keep drafts in their own directory and load it only while serving:

```text
content/
├── posts/
│   └── hello-kiji/index.md
└── drafts/
    └── next-post/index.md
```

```csharp
var showDrafts = builder.Environment.IsDevelopment();
builder.AddMarkdownContent<PostFrontMatter, BlogPost>(BlogPost.Create, options =>
    options.FileFilter = file => showDrafts || file.Directory?.Parent?.Name != "drafts");
```

The filter applies to all of `content/`, so this registration has no `Directory` setting.
`dotnet watch` runs in the `Development` environment and shows the drafts;
`dotnet publish` uses `Production` and leaves them out. See
[Environments](../configuration/#environments).

## Mark the current page in navigation

Compare a link's path with `PageInfo.Url`:

```razor
@inject SiteInfo Site
@inject PageInfo Page

<a href="@Path" aria-current="@(Path == Page.Url.AbsolutePath ? "page" : null)">@ChildContent</a>

@code {
    [Parameter, EditorRequired] public string Href { get; set; } = string.Empty;
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private string Path => Site.ResolveUrl(Href).AbsolutePath;
}
```

Saved as `Components/NavItem.razor`, it is used as `<NavItem Href="about/">About</NavItem>`
and styled with `a[aria-current="page"]`.

## Syntax highlighting

A fenced code block with a language becomes `<pre><code class="language-csharp">`, the
markup that browser highlighters such as [Prism](https://prismjs.com/) expect. Add a
highlighter's stylesheet and scripts to the shared head tags. In Razor, write `@@` for the
`@` in a URL:

```razor
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/prismjs@@1.30.0/themes/prism.min.css" />
<script src="https://cdn.jsdelivr.net/npm/prismjs@@1.30.0/prism.min.js" defer></script>
<script src="https://cdn.jsdelivr.net/npm/prismjs@@1.30.0/plugins/autoloader/prism-autoloader.min.js" defer></script>
```

## Social sharing tags

Add Open Graph tags to the shared `PageHead` component, using `PageInfo` for the page's
public URL:

```razor
@inject PageInfo Page

<HeadContent>
    <link rel="canonical" href="@Page.Url.AbsoluteUri" />
    <meta property="og:title" content="@Title" />
    <meta property="og:description" content="@Description" />
    <meta property="og:url" content="@Page.Url.AbsoluteUri" />
    <meta property="og:type" content="website" />
</HeadContent>
```

Keep these in the same `HeadContent` as the other shared tags, because a second
`HeadContent` replaces the first.

## Generated files

Feeds, sitemaps, and other site-wide files are written after all pages, on every publish.
Only `dotnet publish` writes them; the development server returns 404 for them.

### RSS feed

`AddRssFeed` writes an RSS 2.0 feed from the items you return, in the order you return
them, here newest first:

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

#### Several feeds

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

### Sitemap

`AddSitemap` writes `sitemap.xml` with the absolute URL of every generated page except
`404.html`:

```csharp
using Kiji.Sitemaps;

builder.AddSitemap();
```

To leave pages out, pass their paths relative to `BaseUrl`, percent-encoded or not. Each
path removes only the page with that exact path:

```csharp
builder.AddSitemap(excludedPaths: ["preview/", "internal/status/"]);
```

See [`AddSitemap`](../api-sitemaps/#sitemaphostbuilderextensions).

### robots.txt

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

### JSON index of posts

Write a JSON file for client-side scripts, such as a search box, with `AddArtifact`:

```csharp
using System.Text.Json;

builder.AddArtifact("posts.json", (stream, context, cancellationToken) =>
{
    var posts = context.Services.GetRequiredService<ContentCollection<BlogPost>>()
        .Select(post => new
        {
            title = post.Content.FrontMatter.Title,
            description = post.Content.FrontMatter.Description,
            url = context.Site.ResolveUrl($"posts/{Uri.EscapeDataString(post.Slug)}/").AbsolutePath,
        });
    return JsonSerializer.SerializeAsync(stream, posts, cancellationToken: cancellationToken);
});
```

Load it in the browser from `@Site.ResolveUrl("posts.json").AbsolutePath`.
