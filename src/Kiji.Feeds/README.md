# Kiji.Feeds

RSS 2.0 feed generation for the [Kiji](https://github.com/zzzkan/kiji) static
site generator. The feed is registered as a site artifact and written at build
time, after all pages are rendered.

## Install

```powershell
dotnet add package Kiji.Feeds
```

## Getting started

```csharp
using Kiji.Feeds;

var posts = builder.AddMarkdownContent<FrontMatter>()
    .WithKey(post => post.FileInfo.FileNameWithoutExtension);

app.MapContent<BlogPage, ...>(posts, ...);
app.MapFeed(posts, post => new FeedItem(
    post.FrontMatter.Title!,
    post.FrontMatter.Description!,
    post.FrontMatter.CreatedAt!.Value));
```

Each entry's URL is resolved from its `MapContent` page mapping; items without
a mapped page are skipped. Pass a custom path with
`app.MapFeed(posts, ..., path: "rss/all.xml")`.
