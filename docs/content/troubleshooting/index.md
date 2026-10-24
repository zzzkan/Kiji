---
title: Troubleshooting
description: Fix common setup, development server, and deployment problems.
order: 70
---

## NU1202 or NETSDK1045 mentions .NET 10

Kiji needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
and a project targeting `net10.0` or later. Check `dotnet --version` and
`<TargetFramework>`.

## KIJI1003: Kiji sites require Microsoft.NET.Sdk.Razor

Change the first line of the project file to `<Project Sdk="Microsoft.NET.Sdk.Razor">`.

## No parameter sets were registered

A route such as `/posts/{Slug}/` needs its values from `AddPageParameters<TPage>`. See
[Parameterized routes](../routing/#parameterized-routes).

## The development server returns 404 for every page

`BaseUrl` has a path, so the site is served under it, for example at
<http://localhost:8080/my-site/>. See [Publish under a subpath](../deployment/#publish-under-a-subpath).

## Failed to bind to address: address already in use

Port 8080 is taken. Run `dotnet watch -- --urls http://127.0.0.1:5000` to use another.

## Changes to Program.cs have no effect

`Program.cs` is read only at startup, as `dotnet watch` warns with `ENC0118`. Press `Ctrl+R`
in its terminal to restart.

## A page loses its stylesheet or head tags

The page has a second `HeadContent`, which replaces the shared one. See
[Layouts and the document head](../concepts/#layouts-and-the-document-head).

## Links or images break under a subpath

URLs that start with `/` skip the base path. Use `Site.ResolveUrl` and `Assets` in Razor,
and relative links in Markdown. See [Publish under a subpath](../deployment/#publish-under-a-subpath).
