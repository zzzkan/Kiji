# Kiji

[![CI](https://github.com/zzzkan/kiji/actions/workflows/ci.yml/badge.svg)](https://github.com/zzzkan/kiji/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Kiji.svg)](https://www.nuget.org/packages/Kiji)

A static site generator framework for .NET. Write pages as Razor components, ship static
HTML.

## Quick start

Requires the .NET 10 SDK.

```powershell
dotnet new console -f net10.0 -o MySite
cd MySite
dotnet add package Kiji --version 0.1.0-preview
```

Follow the [getting-started guide](https://zzzkan.github.io/kiji/docs/getting-started/)
to configure the Razor SDK and add your pages. Then `dotnet watch` serves the site and
`dotnet publish -c Release -o dist` generates the static output.

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

### Releasing

The first release is `Kiji` **0.1.0-preview**; `Kiji.Templates` is not included.
Docs continue to use the project reference while the package is unpublished.
The template's package version is substituted during packing.

The [release workflow](.github/workflows/release.yml) uses
[NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing).
Before releasing, create a NuGet.org policy for repository owner `zzzkan`, repository
`kiji`, and workflow file `release.yml` (no environment). Scope it to `Kiji`, allowing
the first package and subsequent versions. Set the GitHub Actions secret `NUGET_USER`
to the policy's NuGet username, not an email address. The workflow no longer uses a
stored `NUGET_API_KEY`.

Versions come from [MinVer](https://github.com/adamralph/minver). After reviewing the
changes and passing CI, tagging the release commit and **pushing the tag publishes
to NuGet.org**. These are release commands, not preparation steps:

```powershell
git tag v0.1.0-preview
git push origin v0.1.0-preview
```

## License

[MIT](LICENSE)
