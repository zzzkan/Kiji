# 📰 Kiji

[![CI](https://github.com/zzzkan/kiji/actions/workflows/ci.yml/badge.svg)](https://github.com/zzzkan/kiji/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Kiji.svg)](https://www.nuget.org/packages/Kiji)
[![License: MIT](https://img.shields.io/github/license/zzzkan/Kiji)](https://github.com/zzzkan/kiji/blob/main/LICENSE)

**Write Razor. Publish a purely static site.** Kiji renders your Razor components with
Blazor's [`HtmlRenderer`](https://learn.microsoft.com/aspnet/core/blazor/components/render-components-outside-of-aspnetcore)
and writes each page, Markdown included, as plain HTML for any static host.

## Why Kiji

### 🧰 Your .NET workflow

Build with Razor, C#, and standard .NET host builders. Preview via `dotnet watch`, publish via `dotnet publish`.

### 📦 Publishing essentials included

Typed Markdown, responsive WebP images, RSS, and sitemaps in one package.

### ⚡ Work on what changed

The browser reloads while you write, and publishing reuses unchanged pages.

### 🔄 Make it yours

Customize Markdown parsing, add your own services and generated files, or replace the image processor.

### 🔕 Static by design

The output needs no .NET server or Blazor runtime. Use JavaScript for browser
interactivity; Razor event handlers such as `@onclick` do not run in the browser.

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
using Microsoft.Extensions.Hosting;

var builder = StaticSite.CreateBuilder(args);
builder.UseSiteInfo(new SiteInfo
{
    BaseUrl = new Uri("https://example.org/"),
    Name = "My Blog",
});

// Register content and pages here.
await builder.Build().RunAsync();
```

Start the development server and open <http://localhost:8080>:

```pwsh
dotnet watch
```

The browser reloads as you edit content and components.

Stop the development server with `Ctrl+C`, then generate the static site in `dist/`:

```pwsh
dotnet publish -o dist
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
