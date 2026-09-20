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
dotnet new console -o MySite
cd MySite
```

Replace `MySite.csproj` so the project can compile Razor components, then reference Kiji:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kiji" Version="0.1.0-preview" />
  </ItemGroup>
</Project>
```

Then write the site definition and its first page:

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
@* Pages/HomePage.razor *@
@page "/"

<h1>Hello from Kiji</h1>
<p>This Razor component is rendered as static HTML.</p>
```

To preview the page locally, start the development server at <http://localhost:8080>:

```pwsh
dotnet watch
```

In addition to Razor and C# hot reload, changes to Markdown, images, and static assets are detected automatically and reflected in the browser.

## Documentation

<https://zzzkan.github.io/kiji/>

The site is itself built with Kiji and lives in [`docs/`](docs), so it doubles as a worked example.

## License

[MIT](LICENSE)
