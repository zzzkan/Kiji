# 📰 Kiji

[![CI](https://github.com/zzzkan/kiji/actions/workflows/ci.yml/badge.svg)](https://github.com/zzzkan/kiji/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Kiji.svg)](https://www.nuget.org/packages/Kiji)

A static site generator for .NET that turns Razor components into static HTML.

## Why Kiji

### 🧰Build with the .NET tools you know

Pages are Razor components with an `@page` route, rendered through Blazor's `HtmlRenderer`. Layouts, injection, and parameters work the way you already know. An ordinary `Program.cs` defines the site with `Create` -> `Add*` / `Use*` -> `RunAsync`; there is no separate Kiji CLI or configuration language.

### 📦Get publishing essentials in one package

Markdown with your own YAML front matter shape, responsive WebP image optimization, RSS feeds, sitemaps, and a live-reloading dev server work together without a set of extension packages to assemble.

### ⚡Fast builds, focused rebuilds

Kiji renders Razor components directly in process and generates pages in parallel. Dependency tracking lets subsequent publishes reuse valid output, while on-demand rendering keeps previews focused on the pages you visit.

### 🔄Develop quickly and adapt when needed

`dotnet watch` shares its rendering path with publishing and reloads the browser when content changes. Extend the Markdig pipeline, post-process HTML, project Markdown into your own model, or replace image processing.

### 🔕One thing it does not do: interactivity

Rendering is one-shot and static, so `@onclick` and `OnAfterRenderAsync` do not survive into the output. The generated site is plain HTML with no Blazor runtime.

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

The server reloads the browser when you change a component or content file.

For the complete walkthrough—including layouts, CSS, Markdown pages, and publishing—see [Getting started](https://kiji-docs.zzzkan.workers.dev/docs/getting-started/).

## Documentation

<https://kiji-docs.zzzkan.workers.dev/>

The site is itself built with Kiji and lives in [`docs/`](docs), so it doubles as a worked example.

## License

[MIT](LICENSE)
