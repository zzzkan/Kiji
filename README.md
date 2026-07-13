# Kiji

[![CI](https://github.com/zzzkan/kiji/actions/workflows/ci.yml/badge.svg)](https://github.com/zzzkan/kiji/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Kiji.svg)](https://www.nuget.org/packages/Kiji)

Kiji is a static site generator framework for .NET. Pages are Razor components
rendered to static HTML via `HtmlRenderer`, assembled with a minimal-API style
builder. A single `Kiji` package includes the markdown content pipeline (YAML
front matter), responsive WebP image optimization, RSS feed and sitemap
artifacts, and a live-reloading on-demand dev server.

## Getting started

```csharp
using Kiji;
using Kiji.Feeds;
using Kiji.Markdown;
using Kiji.Sitemaps;

var builder = KijiApp.CreateBuilder(args);
builder.Site = new SiteInfo
{
    BaseUrl = new Uri("https://example.com"),
    Name = "My Site",
};

// Front matter is site-defined: declare your own shape.
var posts = builder.AddMarkdownContent<PostFrontMatter>()
    .WithKey(post => post.FileInfo.FileNameWithoutExtension)
    .OrderByDescending(post => post.FrontMatter.CreatedAt);

await using var app = builder.Build();

app.MapDefaultLayout<MainLayout>(); // default layout for every page (pages may override via @layout)
app.MapPages(); // every public component with an @page route template in the entry assembly
app.MapNotFound<NotFound>(); // rendered as 404.html

app.MapContent<PostPage, MarkdownContent<PostFrontMatter>>(
    posts,
    post => new { Slug = post.FileInfo.FileNameWithoutExtension },
    post => post.FrontMatter.UpdatedAt); // optional: sitemap <lastmod>

app.MapFeed(posts, async (post, ct) =>
    new FeedItem(post.FrontMatter.Title!, post.FrontMatter.Description!, post.FrontMatter.CreatedAt!.Value)
    {
        ContentHtml = await post.RenderAsync(ct), // optional: <content:encoded>
    });
app.MapSitemap();

return await app.RunAsync(); // build (default) | dev [--port <n>] | preview [--port <n>]
```

- `dotnet run` — builds the site into `dist` incrementally (`--force` for a full
  rebuild, `--verbose` for per-file output)
- `dotnet run dev` — on-demand dev server with live reload; when run under `dotnet watch`, Kiji emits watch-style logs for content/static reload activity
- `dotnet run preview` — serves the built `dist` output

Add `dist/` and `.kiji/` (the build cache) to your site's `.gitignore`.

## Concepts

- **Built-in document shell**: Kiji renders the document itself — the HTML5
  doctype, `<html lang>` from `SiteInfo.Language`, `<head>`, and `<body>`.
  Pages contribute head content (charset meta, `<title>`, metas, links) through
  the `Kiji.Components.Head` component, and `MapDefaultLayout<TLayout>()` sets
  the layout applied to every page (optional; pages may override via `@layout`).
- **Route-declared pages**: `MapPages()` discovers every public component with a
  `@page` route template in the entry assembly — the .NET equivalent of
  file-based routing, since writing `@page` is what makes a component a page.
  Pages in another assembly register via `MapPages(assembly)`.
- **Content collections**: `builder.AddContentSource(...)` /
  `AddMarkdownContent<TFrontMatter>()` declare lazily materialized collections,
  consumable from components via `@inject` and from route mappings via
  `MapContent` / `MapRoutes`.
- **Page-bundle images**: local images referenced from markdown are optimized to
  responsive WebP variants written next to the page's `index.html` and referenced
  with `./`-relative URLs, so sites work at any base path. Encoded variants are
  cached under `.kiji/cache` so unchanged images are never re-encoded. Replace the
  backend by registering your own `IImageAssetProcessor` in `builder.Services`.
- **Trailing slashes**: pages are generated as `route/index.html`; the dev and
  preview servers resolve `/route` and `/route/` to the same page without
  redirecting. Canonical URLs use the trailing-slash form.
- **Artifacts**: RSS feeds and sitemaps are opt-in via `app.MapFeed(...)` /
  `app.MapSitemap()`. Custom site-wide outputs implement `ISiteArtifact` and
  register via `app.MapArtifact(...)`.
- **Markdown pipeline**: customize Markdig, front matter deserialization, and
  HTML post-processing (e.g. heading anchors) via `AddMarkdownContent(options => ...)`.
- **Incremental builds**: `build` records what every page read (content files,
  the content set, options, your site's assemblies) in
  `.kiji/cache/build-manifest.json` and skips pages whose inputs are unchanged —
  editing one post re-renders that post, list pages, and artifacts instead of the
  whole site. Any ambiguity (no manifest, unknown files in `dist`, recompiled
  assemblies, tampered outputs) falls back to a full rebuild; stale output is
  never acceptable. Two assumptions to know about:
  - Renders must be deterministic in their inputs. If a page reads data Kiji
    cannot see (a data file consumed by a custom content loader, an HTTP call),
    declare it with `builder.AddBuildInput("path/to/data")` or
    `builder.AddBuildInput("key", versionValue)` so changes trigger a rebuild —
    or run `dotnet run -- build --force`.
  - Framework (`System.*`/`Microsoft.*`) assemblies are excluded from the change
    fingerprint; after an SDK update, use `--force` if you want to be certain.

## Performance

Pages render in parallel (one `HtmlRenderer`/DI scope per page) straight into
pooled UTF-8 buffers written with a single preallocated write per file, markdown
front matter parses in parallel, and incremental builds skip unchanged pages
entirely. For large sites built in-process (CI, scripts), enabling server GC in
the site's project file typically speeds up full builds:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
</PropertyGroup>
```

Measurement infrastructure lives in the repo: `src/Kiji.Benchmarks`
(BenchmarkDotNet microbenchmarks) and `tools/Kiji.SyntheticSite` (an end-to-end
harness that generates an N-page site and measures full, no-change, and
one-post-edited builds):

```powershell
dotnet run -c Release --project tools/Kiji.SyntheticSite -- --pages 1000 --runs 3
```

## Repository layout

- `src/Kiji`: the framework — routing, rendering, markdown, images, feeds, sitemaps, dev server
- `src/Kiji.Tests`: unit and integration tests
- `src/Kiji.Benchmarks`: BenchmarkDotNet microbenchmarks for the hot paths
- `tools/Kiji.SyntheticSite`: end-to-end build performance harness

An architecture and design document (in Japanese) lives at
[docs/design.md](docs/design.md).

## Build

```powershell
dotnet build
```

## Test

```powershell
dotnet test
```

## Release

Releases are versioned with [MinVer](https://github.com/adamralph/minver) from
git tags and published to NuGet.org by GitHub Actions:

1. Ensure `main` is green and the `NUGET_API_KEY` repository secret is set.
2. Tag the release commit and push the tag:

   ```powershell
   git tag v0.1.0
   git push origin v0.1.0
   ```

3. The [release workflow](.github/workflows/release.yml) builds, tests, packs,
   and pushes the package (with symbol package) to NuGet.org.

For a dry run, tag a prerelease first (e.g. `v0.1.0-preview.1`) and confirm the
listing on NuGet.org before tagging the final version.

## License

[MIT](LICENSE)
