# 📰 Kiji

[![CI](https://github.com/zzzkan/kiji/actions/workflows/ci.yml/badge.svg)](https://github.com/zzzkan/kiji/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Kiji.svg)](https://www.nuget.org/packages/Kiji)

A static site generator for .NET. Write pages as Razor components, ship static HTML. Markdown with your own YAML front matter shape, responsive WebP image optimization, RSS feeds, sitemaps, and a live-reloading dev server are all in this one package.

## Quick start

```powershell
dotnet new console -f net10.0 -o MySite
cd MySite
dotnet add package Kiji --version 0.1.0-preview
```

Follow the [getting-started guide](https://zzzkan.github.io/kiji/docs/getting-started/) to configure the Razor SDK and add your pages. Then `dotnet watch` serves the site and `dotnet publish -c Release -o dist` generates the static output.

## Why Kiji

**Razor components, not a new template language.** Pages are components with an `@page` route, rendered through Blazor's `HtmlRenderer`. Layouts, injection, and parameters work the way you already know. Markdown with your own YAML front matter shape, responsive WebP image optimization, RSS feeds, sitemaps, and a live-reloading dev server are all in the
one `Kiji` package — there is no set of extension packages to assemble.

**Incremental builds.** Editing a post re-renders the pages that depend on it.

**Preview on demand.** The dev server renders the requested page through the same rendering path used by publish, and reloads the browser when content changes.

**One thing it does not do: interactivity.** Rendering is one-shot and static, so `@onclick` and `OnAfterRenderAsync` do not survive into the output. The generated site is plain HTML with no Blazor runtime.

## Documentation

<https://zzzkan.github.io/kiji/>

The site is itself built with Kiji and lives in [`docs/`](docs), so it doubles as a worked example.

## License

[MIT](LICENSE)
