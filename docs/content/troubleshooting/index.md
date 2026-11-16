---
title: Troubleshooting and FAQ
description: Fix common setup, development server, and deployment problems.
order: 70
section: Help
---

## Setup and build

### NU1202 or NETSDK1045 mentions .NET 10

Kiji needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
and a project targeting `net10.0` or later. Check `dotnet --version` and
`<TargetFramework>`.

### KIJI1003: Kiji sites require Microsoft.NET.Sdk.Razor

Change the first line of the project file to `<Project Sdk="Microsoft.NET.Sdk.Razor">`.

### KIJI1001: Kiji cannot generate the site

The project's assembly does not exist, usually because publishing skipped the build with
`--no-build`. Build first, or publish without that option.

### KIJI1002: Kiji generated no files

`Program.cs` finished without running the site. End it with
`await builder.Build().RunAsync();`.

### bin/ takes several hundred megabytes

Add `<UseCurrentRuntimeIdentifier>true</UseCurrentRuntimeIdentifier>` to the project's
`<PropertyGroup>`, as in [Create the project](../getting-started/#create-the-project).

## Development server

### The development server returns 404 for every page

`BaseUrl` has a path, so the site is served under it, for example at
<http://localhost:8080/my-site/>. See [Publish under a subpath](../deployment/#publish-under-a-subpath).

### Failed to bind to address: address already in use

Port 8080 is taken. Run `dotnet watch -- --urls http://127.0.0.1:5000` to use another.

### Changes to Program.cs have no effect

`dotnet watch` applies them only after a restart, as its `ENC0118` warning says. Press
`Ctrl+R` in its terminal.

### feed.xml or sitemap.xml returns 404 while developing

Only `dotnet publish` writes feeds, sitemaps, and other generated files. See
[Feeds and sitemaps](../feeds-and-sitemaps/).

## Pages and content

### No parameter sets were registered

A route such as `/posts/{Slug}/` needs its values from `AddPageParameters<TPage>`. See
[Parameterized routes](../routing/#parameterized-routes).

### A page loses its stylesheet or head tags

The page has a second `HeadContent`, which replaces the shared one. See
[Layouts and the document head](../concepts/#layouts-and-the-document-head).

### Failed to deserialize YAML front matter

The front matter has a key your class does not define, a value of the wrong type, or
malformed YAML. See [Register Markdown content](../markdown/#register-markdown-content).

### Contents directory not found

The directory given to `AddMarkdownContent`, `content/` by default, must exist. See
[Choose the files](../markdown/#choose-the-files).

### A button or @onclick handler does nothing

The output has no Blazor runtime, so event handlers never run. Use JavaScript; see
[Static output](../concepts/#static-output).

## Publishing and deployment

### Links or images break under a subpath

URLs that start with `/` skip the base path. Use `Site.ResolveUrl` and `Assets` in Razor,
and relative links in Markdown. See [Publish under a subpath](../deployment/#publish-under-a-subpath).

### KIJI1004: Set SiteInfo.BaseUrl to host a Kiji site under a path prefix

Remove `StaticWebAssetBasePath` from the site's project file and put the path in
`BaseUrl` instead. See [Publish under a subpath](../deployment/#publish-under-a-subpath).

### KIJI1006: ServiceWorker is outside the project directory or has no PublishedContent

Keep the worker inside the project, normally under `wwwroot/`, and set `PublishedContent`
to the worker to publish. See [Service worker](../deployment/#service-worker).

### A published page shows outdated external data

Kiji reuses a page unless something it tracks has changed, and it does not track files,
HTTP, or the clock. Mark the component with `[AlwaysRender]`; see
[Render pages that use external data](../incremental-builds/#render-pages-that-use-external-data).

### dotnet publish prints no Kiji messages

In a terminal, Kiji's messages, including why a publish failed, appear only with
`dotnet publish -o dist -v d`. See [Logging](../api-kiji/#logging).

## Frequently asked questions

### Does the published site need .NET?

No. The output is HTML, CSS, images, and the files in `wwwroot/`. .NET is needed only to
build the site.

### Do I need Node.js?

No. Building a site needs only the .NET SDK.

### Which .NET versions are supported?

The .NET 10 SDK or later, with a project targeting `net10.0` or later.

### Can I use Razor class libraries?

Yes. Their components render like your own, and their static assets are served under
`_content/{PackageId}/`. Pages in a library need `AddPageAssembly`; see
[Fixed routes](../routing/#fixed-routes). Components that rely on event handlers or
JavaScript interop do not become interactive; see [Blazor APIs](../api-blazor/).

### How do I add interactivity?

With JavaScript in `wwwroot/`, loaded through `Assets`. See
[Static assets](../concepts/#static-assets).

### Is there built-in search?

No. Generate a JSON index with `AddArtifact` and search it with a script; see
[JSON index of posts](../recipes/#json-index-of-posts).

### Where do I report a bug or ask for a feature?

See [Contributing](../contributing/).
