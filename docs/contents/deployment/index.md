---
title: Deployment
description: Publishing to GitHub Pages and other static hosts, including sub-paths.
order: 40
---

`dotnet publish -c Release -o dist` writes a plain directory of files to `dist/`. It holds
the generated site alone — no assemblies, no `deps.json` — so any static host will serve it
as-is.

## Set the published URL

`SiteInfo.BaseUrl` is where the site will live. It feeds canonical URLs, the sitemap, and
the feed — and, if it has a path segment, the base path.

```csharp
app.Info = new SiteInfo
{
    BaseUrl = new Uri("https://example.com/"),
    Name = "My Site",
};
```

## Publishing under a sub-path

A GitHub Pages _project_ site is served from `https://your-name.github.io/repo/`, not the
domain root. Put that whole URL in `BaseUrl`:

```csharp
BaseUrl = new Uri("https://your-name.github.io/my-site/"),
```

Then prefix site-root links with `Site.BaseUrl.AbsolutePath`:

```razor
<a href="@(Site.BaseUrl.AbsolutePath + "docs/")">Docs</a>
<link rel="stylesheet" href="@(Site.BaseUrl.AbsolutePath + "css/app.css")" />
```

`Site.BaseUrl.AbsolutePath` is `/my-site/` here and `/` for a site at the domain root,
so the same concatenation works either way.

What you do _not_ have to touch:

- **Canonical, feed, and sitemap URLs.** They derive from `BaseUrl`.
- **Markdown page-bundle images.** They are relative to the page that uses them.
- **The output layout.** A base path is a deployment location; `dist/` never becomes
  `dist/my-site/`.

The dev server serves under the same prefix, and deliberately returns 404 outside it.
That is on purpose: a link that forgets the base path fails while you are looking at it,
instead of only after you deploy.

See [Markdown and images](../markdown/#page-bundle-images) for image URL rules.

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
          dotnet-version: "10.0.x"

      - run: dotnet publish src/MySite -c Release -o dist

      - uses: actions/configure-pages@v5
      - uses: actions/upload-pages-artifact@v3
        with:
          path: dist
      - id: deployment
        uses: actions/deploy-pages@v4
```

Deploying through Actions does not run Jekyll, so no `.nojekyll` file is needed.

## Other hosts

Netlify, Vercel, Cloudflare Pages, S3, and friends all take a directory. Generate with
`dotnet publish -c Release -o dist` and upload `dist/`.

Two things worth configuring on the host:

- **404s.** `app.UseNotFoundPage<NotFoundPage>()` writes `404.html` at the output root, which
  most hosts serve automatically for unmatched paths.
- **Trailing slashes.** Pages are `route/index.html`. Configure the host to redirect
  `/route` to `/route/` so relative images resolve beside the page. To check before
  deploying, serve `dist/` with whatever your host provides
  locally — `wrangler dev`, `netlify dev`, `npx serve` — since those reproduce the real
  behavior more faithfully than an imitation of it would.

## Build caching in CI

`.kiji/` holds the build manifest and the image cache. Persisting it between CI runs lets
incremental builds skip unchanged pages and skip re-encoding unchanged images. It is
purely an optimization — a missing cache just means a full build.
