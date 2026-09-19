---
title: Getting started
description: Install the package, declare a site, and run the dev server.
order: 10
---

Kiji is a library, not a CLI tool. Your site is an ordinary .NET project that references
it, so there are no Kiji commands to learn: `dotnet watch` runs it, `dotnet publish`
generates it.

In a few minutes, this guide gives you a Razor page, a local development server, and a
static publish output. Add Markdown content and optimized page-bundle images afterward;
they are built into the same package rather than separate extensions.

## Install

Requires the .NET 10 SDK. The initial release is a preview; pin its version explicitly.

```pwsh
dotnet new console -f net10.0 -o MySite
cd MySite
```

Replace `MySite.csproj` with the following. It switches the project to the Razor SDK so
you can write `.razor` files and pins the preview package explicitly:

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

## Build the smallest site

`Program.cs` builds the site the way a minimal-API app builds a web host:

```csharp
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

Create `Pages/HomePage.razor`:

```razor
@page "/"

<h1>Hello from Kiji</h1>
<p>This page is a Razor component rendered to static HTML.</p>
```

This is a complete site. `AddStaticPages()` finds public components with fixed routes in
the entry assembly, so writing `@page "/"` makes `HomePage` the home page.

The small site definition is intentional: configure the site with `Create`, add behavior
with `Add*` and `Use*`, and start it once with `RunAsync`. There is no separate Kiji
configuration language or command-line interface to learn.

Configure `app.Info`, `app.Paths`, and all `Add*` / `Use*` registrations
before calling `RunAsync`. Starting the site makes those settings read-only; later
changes throw. The dev server reloads changed content automatically.

## Run it

| Command                             | What it does                               |
| ----------------------------------- | ------------------------------------------ |
| `dotnet watch`                      | Dev server with live reload and hot reload |
| `dotnet publish -c Release -o dist` | Generates the site into `dist/`            |
| `dotnet clean`                      | Deletes the build cache                    |

The dev server listens on <http://localhost:8080> by default. Run `dotnet publish -c Release -o dist` to generate the site, then upload the contents of `dist/` to your static host.

## Add Markdown pages

A Markdown-backed site adds four pieces to the minimal site:

1. A front matter class defining the site's metadata fields.
2. A model that validates each Markdown file and derives its URL slug.
3. A parameterized Razor component such as `@page "/blog/{Slug}/"`.
4. `UseMarkdownContent` and `AddPages` registrations connecting the content to that page.

See [Markdown and images](../markdown/) for file selection, front matter, rendering, and image behavior.

## Project layout

| Path        | What it is                                                                        |
| ----------- | --------------------------------------------------------------------------------- |
| `contents/` | Markdown content; page bundles may keep one page and its images in each directory |
| `wwwroot/`  | Static assets, copied to the output as-is                                         |
| `dist/`     | The generated site                                                                |
| `.kiji/`    | Build manifest and caches                                                         |

Add `dist/` and `.kiji/` to your `.gitignore`.

The site root — what those relative paths resolve against — defaults to your project
directory. Override it with `app.Paths.RootDirectory` if your layout differs.

## Next

Read [Concepts](../concepts/) for how pages, layouts, and content collections fit
together, or jump to [Deployment](../deployment/) if you want to publish first.
