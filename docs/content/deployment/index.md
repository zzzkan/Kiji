---
title: Deployment
description: Publish to GitHub Pages or another static host, including under a subpath.
order: 50
---

`dotnet publish` writes the site to `dist/` as static files only, ready to upload to any
static host. Before publishing, set `SiteInfo.BaseUrl` to the site's final public URL;
Kiji uses it for page, feed, sitemap, and image URLs.

## Publish under a subpath

When the site is not at the domain root, such as a GitHub Pages project site, include the
path in `BaseUrl`:

```csharp
BaseUrl = new Uri("https://your-name.github.io/my-site/"),
```

Build page links with `Site.ResolveUrl` and asset links with `Assets`; both then include
`/my-site/`. Links written as `/docs/` do not.

```razor
@inject SiteInfo Site

<a href="@(Site.ResolveUrl("docs/").AbsolutePath)">Docs</a>
<link rel="stylesheet" href="@Assets["css/app.css"]" />
```

The path does not become a directory in `dist/`: upload the contents of `dist/` so that
`dist/index.html` is served at `/my-site/`. The development server uses the same path, at
<http://localhost:8080/my-site/>, and returns 404 outside it, so a missing base path shows
up before deployment.

## GitHub Pages with Actions

In the repository settings, set the Pages source to **GitHub Actions**, then add this
workflow, replacing `MySite` with the project directory:

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
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: "10.0.x"

      - run: dotnet publish MySite

      - uses: actions/configure-pages@v6
      - uses: actions/upload-pages-artifact@v5
        with:
          path: MySite/dist
      - id: deployment
        uses: actions/deploy-pages@v5
```

## Other static hosts

On Netlify, Vercel, Cloudflare Pages, and similar services, use `dotnet publish` as the
build command and `dist` as the output directory. The build environment needs the .NET 10
SDK; otherwise, publish in your own CI and upload `dist/`.

Check that the host serves `404.html` for unmatched paths, which `AddNotFoundPage` writes,
and `about/index.html` for `/about/`.

## Compression

Publishing also writes `.gz` and `.br` copies of compressible files for servers that serve
precompressed files, such as nginx with `gzip_static`. GitHub Pages, Netlify, Vercel, and
Cloudflare Pages compress responses themselves, so skip the copies there by adding this to
the project's `<PropertyGroup>`:

```xml
<CompressionEnabled>false</CompressionEnabled>
```
