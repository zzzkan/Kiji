---
title: Deployment
description: Publish to GitHub Pages or another static host, including under a sub-path.
order: 50
---

`dotnet publish -c Release -o dist` writes the generated site to `dist/`. The directory
contains static files only — no assemblies or .NET runtime — and can be uploaded to any
static host.

## Set the published URL

`SiteInfo.BaseUrl` is the public URL of the site. Kiji uses it for canonical URLs, RSS,
sitemaps, and the development server's base path:

```csharp
app.Info = new SiteInfo
{
    BaseUrl = new Uri("https://example.com/"),
    Name = "My Site",
};
```

Set it to the final deployment URL before publishing.

## Publish under a sub-path

A GitHub Pages project site is served from `https://your-name.github.io/repo/`, not from
the domain root. Include the full path in `BaseUrl`:

```csharp
BaseUrl = new Uri("https://your-name.github.io/my-site/"),
```

Prefix links to site-root pages and static assets with `Site.BaseUrl.AbsolutePath`:

```razor
<a href="@(Site.BaseUrl.AbsolutePath + "docs/")">Docs</a>
<link rel="stylesheet" href="@($"{Site.BaseUrl.AbsolutePath}css/app.css")" />
```

`AbsolutePath` is `/my-site/` for that project site and `/` for a domain-root site, so the
same markup works in both places.

Kiji already applies `BaseUrl` to canonical, feed, and sitemap URLs. Markdown page-bundle
images use document-relative URLs and need no prefix. A base path changes where the site
is hosted; it does not add another directory inside `dist/`.

The development server uses the same base path and returns 404 outside it. This makes a
missing prefix visible before deployment. See
[Markdown and images](../markdown/#local-images-and-page-bundles) for image URL behavior.

## GitHub Pages with Actions

In the repository settings, set the Pages source to **GitHub Actions**. Then add a workflow
like this, replacing `MySite` if the project is elsewhere:

```yaml
name: Deploy site

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

      - run: dotnet publish MySite -c Release -o dist

      - uses: actions/configure-pages@v5
      - uses: actions/upload-pages-artifact@v3
        with:
          path: dist
      - id: deployment
        uses: actions/deploy-pages@v4
```

This workflow publishes directly through Actions, so Jekyll and a `.nojekyll` file are
not involved.

## Other static hosts

Netlify, Vercel, Cloudflare Pages, S3, and similar services can all deploy the generated
directory. Use this build command and configure `dist` as the directory to publish:

```pwsh
dotnet publish -c Release -o dist
```

Check two host behaviors:

- `UseNotFoundPage<NotFoundPage>()` writes `404.html` at the output root.
  Most static hosts use that file for unmatched paths.
- Generated pages use `route/index.html`.

If a host provides a local emulator, use it to verify these behaviors before deployment.
