# SITE_NAME

A static site built with [Kiji](https://github.com/zzzkan/kiji).

```powershell
dotnet run dev       # dev server with live reload at http://localhost:8080
dotnet run           # build the site into dist/
dotnet run preview   # serve dist/ the way a static host would
dotnet run clean     # delete dist/ and the build cache
```

## What is here

Deliberately almost nothing. Three pages, one markdown collection, a sitemap, and enough
CSS to be readable.

| Path | What it is |
| --- | --- |
| `Program.cs` | The whole site definition |
| `Pages/` | Components with an `@page` route. Writing `@page` is what makes one a page |
| `Components/` | The layout and the `<head>` contribution |
| `contents/` | Markdown posts, one directory per post, images beside them |
| `wwwroot/` | Static assets, copied to the output as-is |

`contents/hello-world/index.md` is published at `/hello-world/`, because `PostPage.razor`
declares `@page "/{Slug}/"` and `Program.cs` supplies the slugs. Change that route
template to `"/blog/{Slug}/"` and the posts move; nothing else needs to know.

## What is deliberately left out

Kiji supports all of these. They are absent so the starting point stays readable, not
because they are hard.

| | How to add it |
| --- | --- |
| RSS feed | `app.MapFeed(posts, …)` — [Concepts](https://zzzkan.github.io/kiji/docs/concepts/) |
| Tag or archive pages | Another `MapRoutes` over the values you want — [Concepts](https://zzzkan.github.io/kiji/docs/concepts/) |
| Responsive images | Already works; just reference an image from markdown — [Markdown and images](https://zzzkan.github.io/kiji/docs/markdown/) |
| A custom markdown pipeline | `AddMarkdownContent(options => …)` — [Markdown and images](https://zzzkan.github.io/kiji/docs/markdown/) |
| Publishing under a sub-path | Put the path in `SiteInfo.BaseUrl` — [Deployment](https://zzzkan.github.io/kiji/docs/deployment/) |

## Linking

Write links through `Site.Path(...)` rather than hard-coding a leading slash:

```razor
<a href="@Site.Path("hello-world/")">A post</a>
```

That keeps them correct if you publish under a sub-path. `dev` and `preview` serve under
the same prefix, so a link that forgets it fails locally rather than after you deploy.

## A note on the two props files

`Directory.Build.props` and `Directory.Packages.props` are empty stoppers, so this site
builds the same wherever you put it — including inside a repository that has its own.
Delete them to inherit from parent directories again.
