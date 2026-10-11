# 📰 Kiji

[![CI](https://github.com/zzzkan/kiji/actions/workflows/ci.yml/badge.svg)](https://github.com/zzzkan/kiji/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Kiji.svg)](https://www.nuget.org/packages/Kiji)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Write Razor. Publish a purely static site.** Kiji brings your .NET components and Markdown
together in plain HTML, ready for any static host.

<p align="center">
    <img width="720" alt="kiji demo" src="https://github.com/user-attachments/assets/85bffb06-7001-4d41-95e8-c8b92a412b2f" />
</p>

## Demo

- [Kiji documentation](https://kiji-docs.zzzkan.workers.dev/) ([source](docs/))
- [zzzkan.me](https://zzzkan.me/) (the author's blog)

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

## Quick start

```pwsh
dotnet new console -f net10.0 -o MySite
cd MySite
dotnet add package Kiji
```

Change the first line of `MySite.csproj` to use the Razor SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
```

Replace `Program.cs` with the site definition and create `Pages/Home.razor`:

```csharp
// Program.cs
using Kiji;

var app = StaticSite.Create(args);
app.Info = new SiteInfo
{
    BaseUrl = new Uri("https://example.com/"),
    Name = "My Site",
};
app.AddStaticPages();

return await app.RunAsync();
```

```razor
@* Pages/Home.razor *@
@page "/"

<h1>Hello from Kiji</h1>
<p>This page is a Razor component rendered to static HTML.</p>
```

Start the development server and open <http://localhost:8080>:

```pwsh
dotnet watch
```

Generate the static site in `dist/`:

```pwsh
dotnet publish
```

For the complete walkthrough—including layouts, CSS, and Markdown—see [Getting started](https://kiji-docs.zzzkan.workers.dev/docs/getting-started/).

## Contributing

Contributions to Kiji and its documentation are welcome. [Open an issue](https://github.com/zzzkan/kiji/issues) to report a bug or suggest a feature.

## License

[MIT](LICENSE)
