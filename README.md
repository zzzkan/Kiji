# Kiji

[![CI](https://github.com/zzzkan/kiji/actions/workflows/ci.yml/badge.svg)](https://github.com/zzzkan/kiji/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Kiji.svg)](https://www.nuget.org/packages/Kiji)

A static site generator framework for .NET. Write pages as Razor components, ship static
HTML.

## Quick start

```powershell
dotnet new install Kiji.Templates
dotnet new kiji -o MySite
cd MySite
dotnet watch
```

That is a working site on <http://localhost:8080> with live reload. `dotnet publish -o dist`
writes it to `dist/`, which any static host will serve.

The scaffold is deliberately minimal — a home page, a markdown post, a 404 page, and a
sitemap. Feeds, tag pages, and image optimization are all supported and left out of the
starting point; the generated README says where to find each.

## Why Kiji

**Razor components, not a new template language.** Pages are components with an `@page`
route, rendered through Blazor's `HtmlRenderer`. Layouts, injection, and parameters work
the way you already know. Markdown with your own YAML front matter shape, responsive WebP
image optimization, RSS feeds, sitemaps, and a live-reloading dev server are all in the
one `Kiji` package — there is no set of extension packages to assemble.

**Incremental builds.** Editing a post re-renders the pages that depend on it.
See the [performance guide](https://zzzkan.github.io/kiji/docs/performance/) for
external inputs, forced rebuilds, and measurement commands.

**Preview on demand.** The dev server renders the requested page through the same
rendering path used by publish, and reloads the browser when content changes.

**Correctness is the constraint, not an afterthought.** Duplicate routes, output path
collisions, missing route values, and paths escaping the output directory all fail while
planning, before a single file is written. Incremental builds re-render unless they can
prove the previous output is still valid, and anything ambiguous falls back to a full
rebuild.

**One thing it does not do: interactivity.** Rendering is one-shot and static, so
`@onclick` and `OnAfterRenderAsync` do not survive into the output. The generated site is
plain HTML with no Blazor runtime. Bring your own JavaScript, or reach for Blazor
WebAssembly instead.

## Documentation

**<https://zzzkan.github.io/kiji/>** — getting started, concepts, markdown and images,
deployment, and performance.

The site is itself built with Kiji and lives in [`docs/`](docs), so it doubles as a worked
example.

## Contributing

```powershell
dotnet build -c Release
dotnet test -c Release
```

Both operate on the whole solution. Pass no extra flags to `dotnet test` — unrecognized
ones reach the Microsoft.Testing.Platform runner, which prints help and exits non-zero.

Design constraints, alternatives already evaluated and rejected, conventions, and gotchas
are recorded in [AGENTS.md](AGENTS.md). Read it before changing the rendering, generation,
or incremental build paths.

Releases are versioned by [MinVer](https://github.com/adamralph/minver) from git tags. Tag
a release commit and push the tag; the
[release workflow](.github/workflows/release.yml) builds, tests, packs, and pushes `Kiji`
and `Kiji.Templates` to NuGet.org.

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## License

[MIT](LICENSE)
