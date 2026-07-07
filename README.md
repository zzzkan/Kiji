# Kiji

[![CI](https://github.com/zzzkan/kiji/actions/workflows/ci.yml/badge.svg)](https://github.com/zzzkan/kiji/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Kiji.svg)](https://www.nuget.org/packages/Kiji)

Kiji is a static site generator framework for .NET. Pages are Razor components
rendered to static HTML via `HtmlRenderer`, assembled with a minimal-API style
builder, with a built-in on-demand dev server.

## Packages

| Package | Description |
| --- | --- |
| `Kiji` | Core: routing, rendering, site artifacts, dev server |
| `Kiji.Markdown` | Markdown content pipeline with YAML front matter |
| `Kiji.Images` | Responsive WebP image optimization (ImageSharp) |
| `Kiji.Feeds` | RSS feed generation (`app.MapFeed(...)`) |
| `Kiji.Sitemaps` | Sitemap generation (`app.MapSitemap()`) |

## Getting started

```csharp
using Kiji;
using Kiji.Feeds;
using Kiji.Images;
using Kiji.Markdown;
using Kiji.Sitemaps;

var builder = KijiApp.CreateBuilder(args);
builder.Site = new SiteInfo
{
    BaseUrl = new Uri("https://example.com"),
    Name = "My Site",
};
builder.AddImageOptimization();

var posts = builder.AddMarkdownContent<FrontMatter>()
    .WithKey(post => post.FileInfo.FileNameWithoutExtension)
    .OrderByDescending(post => post.FrontMatter.CreatedAt);

await using var app = builder.Build();
app.MapPages<Root>();
app.MapNotFound<NotFound>();
app.MapContent<BlogPage, MarkdownContent<FrontMatter>>(posts, post => new { Slug = ... });
app.MapFeed(posts, post => new FeedItem(post.FrontMatter.Title!, post.FrontMatter.Description!, post.FrontMatter.CreatedAt!.Value));
app.MapSitemap();

return await app.RunAsync(); // build (default) | clean | serve [--port <n>] | preview [--port <n>]
```

RSS feeds and sitemaps are opt-in: call `app.MapFeed(...)` / `app.MapSitemap()`
explicitly. Custom site-wide outputs implement `ISiteArtifact` and register via
`app.MapArtifact(...)`. Per-file markdown post-processing (e.g. heading anchor
links) hooks in via `AddMarkdownContent(options => ...)` — see the
[Kiji.Markdown README](src/Kiji.Markdown/README.md).

## Repository layout

- `src/Kiji`: core SSG runtime, routing, rendering, site artifacts, and dev server
- `src/Kiji.Markdown`: markdown content pipeline and extensions
- `src/Kiji.Images`: responsive image generation support
- `src/Kiji.Feeds`: RSS feed artifact
- `src/Kiji.Sitemaps`: sitemap artifact
- `src/Kiji.Tests`: unit and integration tests for the Kiji stack

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
   and pushes the packages (with symbol packages) to NuGet.org.

For a dry run, tag a prerelease first (e.g. `v0.1.0-preview.1`) and confirm the
listing on NuGet.org before tagging the final version.

## License

[MIT](LICENSE)
