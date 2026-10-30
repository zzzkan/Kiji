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

Check that the host serves `about/index.html` for `/about/` and uses `404.html`, written by
`UseNotFoundPage`, for unmatched paths.

## Compression

Kiji can also precompress your site. Add this to the project's `<PropertyGroup>`:

```xml
<CompressionEnabled>true</CompressionEnabled>
```

Publishing then writes `.gz` and `.br` copies of HTML, CSS, JavaScript, JSON, XML, SVG, and
text files next to the originals. Servers configured to send precompressed files, such as
nginx with `gzip_static` or Caddy with `precompressed`, send these copies as is instead of
compressing each response. Editing a file in `dist/` leaves its copies out of date, so change
the site and publish again.

GitHub Pages, Netlify, Vercel, and Cloudflare Pages compress responses themselves, so
leave the setting off for them; it is off by default.

## Service worker

A service worker can cache the site for offline reading. Declare it as in a Blazor project,
including `PublishedContent`, the worker to publish:

```xml
<PropertyGroup>
  <ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest>
</PropertyGroup>
<ItemGroup>
  <ServiceWorker Include="wwwroot/service-worker.js"
                 PublishedContent="wwwroot/service-worker.published.js" />
</ItemGroup>
```

Publishing writes `service-worker-assets.js`, which lists every published page and asset
with its SHA-256 hash. It also publishes `service-worker.published.js` as
`service-worker.js`, with a first line that names the manifest version. The version changes
whenever a published file does. Load the list in the published worker with
`self.importScripts('./service-worker-assets.js')`. `dotnet watch` serves
`wwwroot/service-worker.js` instead.
