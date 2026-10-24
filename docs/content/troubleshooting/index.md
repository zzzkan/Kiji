---
title: Troubleshooting
description: Fix common setup, development server, and deployment problems.
order: 70
---

Each section starts with the symptom or message you see.

## The package or project targets an unsupported .NET version

`dotnet add package Kiji` reports `NU1202` (the package is not compatible with your target
framework), or the build reports `NETSDK1045` (the SDK does not support .NET 10.0).

Kiji requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later,
and the project must target `net10.0` or later. Run `dotnet --version` in the project
directory to see the selected SDK, and check `<TargetFramework>` in the project file.

## KIJI1003: Kiji sites require Microsoft.NET.Sdk.Razor

The project uses the plain .NET SDK. Change the first line of the project file:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
```

## A route requires route values, but no parameter sets were registered

A component has a parameterized route such as `/posts/{Slug}/`, and `Program.cs` does not
supply its values. Register them with `AddPageParameters<TPage>`, or return an empty
sequence to generate no pages. See [Parameterized routes](../routing/#parameterized-routes).

## The development server returns 404 for every page

The message says the site is served under a path because `SiteInfo.BaseUrl` has one. Open
that path instead, for example <http://localhost:8080/my-site/> for
`https://example.com/my-site/`. See [Publish under a subpath](../deployment/#publish-under-a-subpath).

## Failed to bind to address: address already in use

Another program is using port 8080. Choose another address:

```pwsh
dotnet watch -- --urls http://127.0.0.1:5000
```

## Changes to Program.cs have no effect

`dotnet watch` applies component and content changes while running, but `Program.cs` is
read only when the site starts. Its warning `ENC0118` says the same. Press `Ctrl+R` in the
terminal running `dotnet watch`, or stop it with `Ctrl+C` and run it again.

## A page loses its stylesheet or head tags

Separate `HeadContent` blocks replace each other instead of combining. Keep shared and
page-specific tags in one `HeadContent`, for example through a shared component as in
[Getting started](../getting-started/#add-a-layout-and-css).

## Links or images break after deployment under a subpath

A URL that starts with `/` points at the domain root, not the site's base path. In Razor,
use `Site.ResolveUrl` for page links and `Assets` for static files. In Markdown, prefer
document-relative links. See [Deployment](../deployment/#publish-under-a-subpath).
