# 📰 Kiji

[![CI](https://github.com/zzzkan/kiji/actions/workflows/ci.yml/badge.svg)](https://github.com/zzzkan/kiji/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Kiji.svg)](https://www.nuget.org/packages/Kiji)
[![License: MIT](https://img.shields.io/github/license/zzzkan/Kiji)](https://github.com/zzzkan/kiji/blob/main/LICENSE)

**Write Razor. Publish a purely static site.** Kiji brings your .NET components and Markdown
together in plain HTML, ready for any static host.

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

Kiji requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later.

```pwsh
dotnet new install Kiji.Templates
dotnet new kiji-blog -o MyBlog
cd MyBlog
```

Edit your site settings in `Program.cs`:

```csharp
//Program.cs

app.Info = new SiteInfo
{
    BaseUrl = new Uri("https://example.org/"),
    Name = "My Blog",
};
```

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

| Demo                                                                                                                                                                                              | Image                                                                                                                  | Create a site |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- | --- |
| [Kiji Blog Template](https://zzzkan.github.io/Kiji.Templates/kiji-blog/) ([source](https://github.com/zzzkan/Kiji.Templates/tree/main/src/Kiji.Templates/content/Kiji.Blog))                      | ![Kiji Blog template](https://raw.githubusercontent.com/zzzkan/Kiji.Templates/main/images/kiji-blog.png)               | `dotnet new kiji-blog -o MyBlog` |
| [Kiji Doc Template](https://zzzkan.github.io/Kiji.Templates/kiji-doc/) ([source](https://github.com/zzzkan/Kiji.Templates/tree/main/src/Kiji.Templates/content/Kiji.Doc))                         | ![Kiji Doc template](https://raw.githubusercontent.com/zzzkan/Kiji.Templates/main/images/kiji-doc.png)                 | `dotnet new kiji-doc -o MyDocs` |
| [Kiji Interactive Template](https://zzzkan.github.io/Kiji.Templates/kiji-interactive/) ([source](https://github.com/zzzkan/Kiji.Templates/tree/main/src/Kiji.Templates/content/Kiji.Interactive)) | ![Kiji Interactive template](https://raw.githubusercontent.com/zzzkan/Kiji.Templates/main/images/kiji-interactive.png) | `dotnet new kiji-interactive -o MyInteractive` |
| [Kiji Fluent Template](https://zzzkan.github.io/Kiji.Templates/kiji-fluent/) ([source](https://github.com/zzzkan/Kiji.Templates/tree/main/src/Kiji.Templates/content/Kiji.Fluent))                | ![Kiji Fluent template](https://raw.githubusercontent.com/zzzkan/Kiji.Templates/main/images/kiji-fluent.png)           | `dotnet new kiji-fluent -o MyFluent` |

## Contributing

Contributions to Kiji and its documentation are welcome. [Open an issue](https://github.com/zzzkan/kiji/issues) to report a bug or suggest a feature.

## License

[MIT](https://github.com/zzzkan/kiji/blob/main/LICENSE)
