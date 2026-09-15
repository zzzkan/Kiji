# SITE_NAME

A static site built with [Kiji](https://github.com/zzzkan/kiji).

```powershell
dotnet watch                         # dev server with live reload at http://localhost:8080
dotnet publish -c Release -o dist    # generate the site into dist/
dotnet clean                         # delete the build cache
```

## What is here

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

## Adding features

| | How to add it |
| --- | --- |
| RSS feed | `app.AddRssFeed(services => …)` — [Concepts](https://zzzkan.github.io/kiji/docs/concepts/) |
| Tag or archive pages | Another `AddPages` over the values you want — [Concepts](https://zzzkan.github.io/kiji/docs/concepts/) |
| Responsive images | Already works; just reference an image from markdown — [Markdown and images](https://zzzkan.github.io/kiji/docs/markdown/) |
| A custom markdown pipeline | `UseMarkdownContent(key: …, configure: …)` — [Markdown and images](https://zzzkan.github.io/kiji/docs/markdown/) |
| Publishing under a sub-path | Put the path in `SiteInfo.BaseUrl` — [Deployment](https://zzzkan.github.io/kiji/docs/deployment/) |

## Linking

Write links through `Site.Path(...)` rather than hard-coding a leading slash:

```razor
<a href="@Site.Path("hello-world/")">A post</a>
```

That keeps them correct if you publish under a sub-path. The dev server serves under the
same prefix, so a link that forgets it fails locally rather than after you deploy.

## A note on the two props files

`Directory.Build.props` and `Directory.Packages.props` are empty stoppers, so this site
builds the same wherever you put it — including inside a repository that has its own.
Delete them to inherit from parent directories again.
