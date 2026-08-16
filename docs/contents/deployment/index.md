---
title: Deployment
description: Publishing to GitHub Pages and other static hosts, including sub-paths.
order: 40
---

`dotnet run` writes a plain directory of files to `dist/`. Any static host will serve it.

## Set the published URL

`SiteInfo.BaseUrl` is where the site will live. It feeds canonical URLs, the sitemap, and
the feed — and, if it has a path segment, the base path.

```csharp
builder.Site = new SiteInfo
{
    BaseUrl = new Uri("https://example.com/"),
    Name = "My Site",
};
```

## Publishing under a sub-path

A GitHub Pages *project* site is served from `https://your-name.github.io/repo/`, not the
domain root. Put that whole URL in `BaseUrl`:

```csharp
BaseUrl = new Uri("https://your-name.github.io/my-site/"),
```

Then write your own links through `Site.Path`:

```razor
<a href="@Site.Path("docs/")">Docs</a>
<link rel="stylesheet" href="@Site.Path("css/app.css")" />
```

`Site.Path("css/app.css")` returns `/my-site/css/app.css` here, and `/css/app.css` for a
site at the domain root — so the same markup works either way.

What you do *not* have to touch:

- **Canonical, feed, and sitemap URLs.** They derive from `BaseUrl`.
- **Markdown page-bundle images.** They are relative to the page that uses them.
- **The output layout.** A base path is a deployment location; `dist/` never becomes
  `dist/my-site/`.

`dev` and `preview` serve under the same prefix, and deliberately return 404 outside it.
That is on purpose: a link that forgets `Site.Path` fails while you are looking at it,
instead of only after you deploy.

> Kiji never emits a `<base>` element. It would re-root the document-relative image URLs
> above and break them.

## GitHub Pages with Actions

Set the repository's Pages source to **GitHub Actions** (Settings → Pages), then:

```yaml
name: Deploy docs

on:
  push:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: pages
  cancel-in-progress: true

jobs:
  deploy:
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - run: dotnet run --project src/MySite -c Release -- build

      - uses: actions/configure-pages@v5
      - uses: actions/upload-pages-artifact@v3
        with:
          path: src/MySite/dist
      - id: deployment
        uses: actions/deploy-pages@v4
```

Deploying through Actions does not run Jekyll, so no `.nojekyll` file is needed.

## Other hosts

Netlify, Vercel, Cloudflare Pages, S3, and friends all take a directory. Build with
`dotnet run -- build` and publish `dist/`.

Two things worth configuring on the host:

- **404s.** `app.MapNotFound<NotFoundPage>()` writes `404.html` at the output root, which
  most hosts serve automatically for unmatched paths.
- **Trailing slashes.** Pages are `route/index.html`. Hosts generally resolve `/route` to
  it already; `preview` reproduces that behavior locally so you can check.

## Build caching in CI

`.kiji/` holds the build manifest and the image cache. Persisting it between CI runs lets
incremental builds skip unchanged pages and skip re-encoding unchanged images. It is
purely an optimization — a missing cache just means a full build.
