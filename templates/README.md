# Kiji.Templates

The `dotnet new` project template for [Kiji](https://github.com/zzzkan/kiji), a static
site generator framework for .NET. Pages are Razor components rendered to static HTML.

## Install

```powershell
dotnet new install Kiji.Templates
```

## Create a site

```powershell
dotnet new kiji -o MySite
cd MySite
dotnet watch
```

That scaffolds a working site and starts the dev server with live reload at
<http://localhost:8080>. `dotnet publish -c Release -o dist` generates it into `dist`.

The scaffold is deliberately minimal: a home page listing your posts, a post page, a 404
page, one markdown post, and a sitemap. Feeds, tag pages, and image optimization are all
supported by Kiji and left out of the starting point — the generated README says where to
find each one.

### Options

| Option | Default | Description |
| --- | --- | --- |
| `--siteName` | `My Kiji Site` | The site name, used in titles, the feed, and the sitemap. |
| `--baseUrl` | `https://example.com/` | The published base URL. |

If you are publishing to a sub-path — a GitHub Pages project site, for example — include
it in the base URL:

```powershell
dotnet new kiji -o MySite --baseUrl https://your-name.github.io/my-site/
```

Kiji then serves the dev server under that same prefix, so what you browse locally
matches what you deploy. Write your own links through `Site.Path("css/app.css")`, which
resolves under the prefix.

See the [project README](https://github.com/zzzkan/kiji) for the full walkthrough.

## Note on MSBuild inheritance

The scaffolded site ships an empty `Directory.Build.props` and a
`Directory.Packages.props` that turns off central package management, so it builds the
same way wherever you put it — including inside a repository that has its own. Delete
them if you want the site to inherit from its parent directories.

## License

[MIT](https://github.com/zzzkan/kiji/blob/main/LICENSE)
