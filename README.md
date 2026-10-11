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
app.AddPages();

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

### Dependency maintenance

[Dependabot](.github/dependabot.yml) checks NuGet packages, GitHub Actions, and the
.NET SDK each weekday at 02:00 Asia/Tokyo. Version updates wait three days after
release; security updates do not wait for that cooldown. Major updates are allowed
and require review. Version update coverage and grouping are listed below.

| Dependencies | Version source | Update handling |
| --- | --- | --- |
| NuGet packages | `Directory.Packages.props` | Up to 10 open PRs; xUnit and Microsoft.Testing.Platform update together, as do SkiaSharp and its native assets |
| GitHub Actions | `.github/workflows/*.yml` | SHA pins and version comments are updated; minor/patch updates are grouped, major updates are separate |
| .NET SDK | `global.json` | SDK updates get their own PRs |
| Wrangler CLI | `wranglerVersion` in `.github/workflows/docs-deploy.yml` | Manual: Dependabot does not update Action input values |
| Prism.js | CDN URLs in `docs/Components/PageHead.razor` | Manual: Dependabot does not update these URLs; update all five references together |

Review the [Dependabot update jobs](https://github.com/zzzkan/kiji/network/updates),
open dependency PRs, and [security alerts](https://github.com/zzzkan/kiji/security/dependabot)
weekly, and check [Wrangler releases](https://github.com/cloudflare/workers-sdk/releases)
and [Prism releases](https://github.com/PrismJS/prism/releases) at the same time.
Investigate failed jobs and blocked PRs rather than treating an empty PR list as
proof that everything is current. Keep new dependencies in a supported manifest;
document any exceptions in this table.

Before merging an update, review its release notes and require successful CI:
Release builds and tests on Linux and Windows, package compatibility validation,
and docs publishing. For manual Wrangler updates, also run a deployment dry run;
for Prism updates, check syntax highlighting and line highlighting in the docs.
Keep Actions pinned to full commit SHAs with version comments. Updates are merged
after review, not automatically.

## License

[MIT](LICENSE)
