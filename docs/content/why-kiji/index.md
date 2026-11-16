---
title: Why Kiji
description: What Kiji is for, when it fits, and when another tool is a better choice.
order: 15
section: Introduction
---

Kiji is a static site generator for .NET developers. Pages are Razor components, site
setup is a standard .NET host in `Program.cs`, and the result is plain HTML that any
static host can serve.

## What you get

- **Razor components as pages.** Layouts, child components, dependency injection, and
  scoped CSS work as in other Razor projects. See [Concepts](../concepts/).
- **Typed Markdown.** Front matter is read into your own class, and local images become
  responsive WebP images. See [Markdown and images](../markdown/).
- **The .NET commands you know.** `dotnet watch` serves a preview that reloads as you
  edit, and `dotnet publish` writes the site.
- **Fast repeat publishes.** Pages whose inputs did not change are reused. See
  [Incremental builds](../incremental-builds/).
- **Feeds and sitemaps.** RSS, sitemaps, and your own generated files come from the same
  package. See [Feeds and sitemaps](../feeds-and-sitemaps/).

## When Kiji fits

- Blogs, documentation, and project or landing pages whose content is known when you
  publish.
- Teams that already use C# and Razor and want to share models, components, and Razor
  class libraries with the site.
- Sites hosted as static files, for example on GitHub Pages or a CDN.

## When to choose something else

- **Pages that depend on the request**, such as sign-in, per-user content, or form
  handling on the server. Use ASP.NET Core, for example a Blazor Web App.
- **Applications driven by Razor event handlers.** Kiji's output has no Blazor runtime,
  so `@onclick` and similar handlers never run. Add browser behavior with JavaScript, or
  use Blazor WebAssembly for an application that runs in the browser.
- **Routes that are not known in advance.** Kiji writes one file per page, so optional,
  constrained, and catch-all route parameters are rejected. See
  [Route limits](../routing/#route-limits).
- **Projects without .NET.** Building a site needs the .NET 10 SDK or later.

Kiji is in preview, so its APIs can still change between releases.

## Compared with Blazor hosting models

Kiji uses Blazor's components and renderer, but not its hosting models:

|                               | Kiji            | Blazor Web App (static server rendering)  | Blazor WebAssembly  |
| ----------------------------- | --------------- | ----------------------------------------- | ------------------- |
| When HTML is rendered         | At publish      | On each request                           | In the browser      |
| What serves the site          | Any static host | An ASP.NET Core server                    | Any static host     |
| .NET runtime in the browser   | No              | No                                        | Yes                 |
| Event handlers like `@onclick` | Never run      | Run only with an interactive render mode  | Run                 |
