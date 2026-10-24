---
title: Deployment
description: Publish to GitHub Pages or another static host, including under a sub-path.
order: 50
---

`dotnet publish` writes the generated site to `dist/`. The directory
contains static files only — no assemblies or .NET runtime — and can be uploaded to any
static host.

## Set the published URL

`SiteInfo.BaseUrl` is the public URL of the site. Kiji uses it to resolve page URLs,
RSS and sitemap links, and the development server's base path:

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

Use `Site.ResolveUrl` for page links and `Assets` for static asset links:

```razor
@inject SiteInfo Site

<a href="@(Site.ResolveUrl("docs/").AbsolutePath)">Docs</a>
<link rel="stylesheet" href="@Assets["css/app.css"]" />
```

Both URLs include `/my-site/`.
A single leading `/` is optional: `Site.ResolveUrl("/docs/")` also preserves the deployment
base path.

Kiji already applies `BaseUrl` to feed, sitemap, and generated Markdown image
URLs. Page-bundle image URLs include the deployment base path, while their files remain
beside the page inside `dist/`. A base path does not add another directory inside `dist/`.

The development server uses the same base path and returns 404 outside it. With the
example above, open <http://localhost:8080/my-site/> after starting `dotnet watch`.
This makes a missing prefix visible before deployment. See
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

GitHub Pages compresses responses itself, so you can
[skip the compressed copies](#compression).

## Other static hosts

Netlify, Vercel, Cloudflare Pages, S3, and similar services can all deploy the generated
directory. Use this build command and configure `dist` as the directory to publish:

```pwsh
dotnet publish
```

If the host runs the build, its build environment needs the .NET 10 SDK or later. Otherwise,
publish in your own CI and upload `dist/`.

Before deploying to a new host, check that it:

- Serves `404.html` from the output root for unmatched paths. `AddNotFoundPage<NotFoundPage>()`
  writes this file, and most static hosts use it automatically.
- Serves `about/index.html` for `/about/`. Kiji writes every page as `route/index.html`.

If a host provides a local emulator, use it to verify these behaviors before deployment.

## Compression

By default, publishing also writes Gzip (`.gz`) and Brotli (`.br`) copies of compressible
files. They help only on a server configured to serve precompressed files, such as nginx
with `gzip_static`; configure it to send them with the matching `Content-Encoding` header.

Hosted services such as GitHub Pages, Netlify, Vercel, and Cloudflare Pages compress
responses themselves, so the copies are unnecessary there. To skip them, add this to the
project's `<PropertyGroup>`:

```xml
<CompressionEnabled>false</CompressionEnabled>
```

To skip them for a single publish, run `dotnet publish -p:CompressionEnabled=false`.
