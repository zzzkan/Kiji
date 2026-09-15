# Kiji

A static site generator framework for .NET. Write pages as Razor components, ship static
HTML. Markdown with your own YAML front matter shape, responsive WebP image optimization,
RSS feeds, sitemaps, and a live-reloading dev server are all in this one package.

## Quick start

```powershell
dotnet new install Kiji.Templates
dotnet new kiji -o MySite
cd MySite
dotnet watch
```

That is a working site on <http://localhost:8080> with live reload. `dotnet publish -o dist` writes it
into `dist/`, which any static host will serve.

## Or add it to an existing project

```powershell
dotnet add package Kiji
```

Use the [getting-started guide](https://zzzkan.github.io/kiji/docs/getting-started/)
for project configuration, pages, and the document shell.

Add `dist/` and `.kiji/` to your `.gitignore`.

## Documentation

<https://zzzkan.github.io/kiji/> — getting started, concepts, markdown and images,
deployment, and performance. The site is itself built with Kiji.
