---
title: Blazor APIs
description: ASP.NET Core components and services in static pages.
order: 160
section: API reference
---

Kiji renders components to static HTML without a Blazor runtime in the browser.

## `PageTitle` and `HeadContent`

Use Blazor's `PageTitle` and `HeadContent` to fill `<head>`. A second `HeadContent`
replaces the first instead of adding to it. See
[Layouts and the document head](../concepts/#layouts-and-the-document-head).

## `ComponentBase.Assets`

`Assets["css/app.css"]` returns the asset's public URL with the base path and, when
available, a fingerprint. Unknown paths are returned unchanged. URLs written directly in
CSS, JavaScript, or HTML are not rewritten.

## `ImportMap`

Place Blazor's `<ImportMap />` in `HeadContent` before scripts that use JavaScript imports.
See the
[ImportMap component](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0#importmap-component).

## `NavigationManager` and `NavLink`

Both render, so Razor class libraries that use them still work. `NavigationManager.Uri` is
the page's public URL, and `NavLink` marks the current page as active. `NavigateTo` and
`Refresh` throw; configure redirects on your host. In your own components, prefer
`PageInfo`, `SiteInfo`, and `<a>`.

## `IJSRuntime`

`IJSRuntime` can be injected, but its calls throw `InvalidOperationException`.

## Event handlers and after-render methods

Event handlers such as `@onclick` never run in the browser, and `OnAfterRender` and
`OnAfterRenderAsync` never run at all. Use a `<script>` element or a JavaScript module for
browser behavior.
