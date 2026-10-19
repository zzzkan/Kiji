# 📰 Kiji

[![CI](https://github.com/zzzkan/kiji/actions/workflows/ci.yml/badge.svg)](https://github.com/zzzkan/kiji/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Kiji.svg)](https://www.nuget.org/packages/Kiji)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/zzzkan/kiji/blob/main/LICENSE)

**Write Razor. Publish a purely static site.** Kiji brings your .NET components and Markdown
together in plain HTML, ready for any static host.

<p align="center">
    <img width="720" alt="kiji demo" src="https://github.com/user-attachments/assets/85bffb06-7001-4d41-95e8-c8b92a412b2f" />
</p>

## Why Kiji

### 🧰 Your .NET workflow

Razor pages, layouts, and C# models. Preview with `dotnet watch`; ship with
`dotnet publish`.

### 📦 Publishing essentials included

Typed Markdown, responsive WebP images, RSS, and sitemaps in one package.

### ⚡ Work on what changed

Live browser reloads while you write; unchanged pages can be reused when you publish.

### 🔄 Make it yours

Customize Markdown, transform HTML, or bring your own content source and image processor.

### 🔕 Static by design

The output needs no .NET server or Blazor runtime. Use JavaScript for browser
interactivity; Razor event handlers such as `@onclick` are not included.

## Getting started

```pwsh
dotnet new install Kiji.Templates
dotnet new kiji-blog -o MyBlog
cd MyBlog
```

Edit `SiteInfo` in `Program.cs` to set your site name, author, and public `BaseUrl`,
including any deployment subpath. The template includes dated Markdown posts, an
article list, shared styles, and RSS.

Start the development server and open <http://localhost:8080>:

```pwsh
dotnet watch
```

The development server reflects content changes immediately and reloads your browser automatically.

Stop the development server with `Ctrl+C`, then generate the static site in `dist/`:

```pwsh
dotnet publish
```

See [Kiji.Templates](https://github.com/zzzkan/Kiji.Templates) for all templates and options,
or follow [Getting started](https://kiji-docs.zzzkan.workers.dev/docs/getting-started/)
to use a template or build a minimal blog from scratch.

## Demo

- [Kiji documentation site](https://kiji-docs.zzzkan.workers.dev/) ([source code](https://github.com/zzzkan/kiji/tree/main/docs))
- [Blog template with Markdown posts](https://zzzkan.github.io/Kiji.Templates/kiji-blog/)
- [Blazor WebAssembly template recreated with Kiji and JavaScript](https://zzzkan.github.io/Kiji.Templates/kiji-interactive/)
- [Author's personal blog (zzzkan.me)](https://zzzkan.me/)

## Contributing

Contributions to Kiji and its documentation are welcome. [Open an issue](https://github.com/zzzkan/kiji/issues) to report a bug or suggest a feature.

## License

[MIT](https://github.com/zzzkan/kiji/blob/main/LICENSE)
