---
title: Kiji namespace
navTitle: Kiji
description: The site builder, site and page metadata, content collections, and site-wide files.
order: 110
section: API reference
---

## `StaticSite`

Creates a standard `HostApplicationBuilder` with Kiji's project-local configuration:

```csharp
using Microsoft.Extensions.Hosting;

var builder = StaticSite.CreateBuilder(args);
builder.UseSiteInfo(new SiteInfo
{
    BaseUrl = new Uri("https://example.com/"),
    Name = "My Site",
});

// builder.Services, Add* and Use* registrations
await builder.Build().RunAsync();
```

| Member                                                       | Behavior                                                                                                                                                     |
| ------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `static HostApplicationBuilder CreateBuilder()`              | Creates a builder without development server arguments.                                                                                                      |
| `static HostApplicationBuilder CreateBuilder(string[] args)` | Creates a builder for the current project. While serving, arguments configure ASP.NET Core, including `--environment` and `--urls`. Publishing ignores them. |

It serves during `dotnet watch` and generates during `dotnet publish`. A publish or
development server failure logs the error under `Kiji.Build`, and `RunAsync` then throws
it, so the process exits with a failure code. A canceled publish, or one still running at
the host's shutdown timeout, throws `OperationCanceledException` because the output is
incomplete.

## `StaticSiteHostBuilderExtensions`

Registration methods for a builder created by `StaticSite.CreateBuilder`; other builders
are rejected. Use the builder's standard [Services](#services), [Environment](#environment),
[Configuration](#configuration), and [Logging](#logging) as well.

| Extension method                                                                                         | Behavior                                                                                                 |
| -------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `UseSiteInfo(SiteInfo siteInfo)`                                                                         | Sets the required site metadata. A later call replaces it.                                               |
| `UseDefaultLayout<TLayout>()`                                                                            | Sets the layout for pages without their own `@layout`.                                                   |
| `AddPageAssembly(Assembly assembly)`                                                                     | Adds pages from another assembly without duplicates. The entry assembly is included automatically.       |
| `AddPageParameters<TPage>(Func<IServiceProvider, IEnumerable<object>> parameters)`                       | Adds parameter sets. See [Page parameters](#page-parameters).                                            |
| `UseNotFoundPage<TPage>()`                                                                               | Writes the component as `404.html`. It needs exactly one route and cannot receive page parameters.       |
| `AddArtifact(string outputRelativePath, Func<Stream, SiteOutputContext, CancellationToken, Task> write)` | Writes a file after all pages. Do not dispose the stream. See [`SiteOutputContext`](#siteoutputcontext). |

Extension methods in other namespaces register content and generated files:

| Method                                                                            | Namespace       |
| --------------------------------------------------------------------------------- | --------------- |
| [`AddMarkdownContent`, `AddKeyedMarkdownContent`](../api-markdown/#markdownhostbuilderextensions) | `Kiji.Markdown` |
| [`AddRssFeed`](../api-feeds/#rssfeedhostbuilderextensions)                                     | `Kiji.Feeds`    |
| [`AddSitemap`](../api-sitemaps/#sitemaphostbuilderextensions)                                     | `Kiji.Sitemaps` |

Only `dotnet publish` writes artifacts, feeds, and sitemaps; the development server
returns 404 for them.

### Services

`Services` is a standard
[.NET dependency injection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview)
collection:

```csharp
builder.Services.AddScoped<RelatedPosts>();
```

- Each page render has its own scope, shared by its page, layout, and child components.
- Page parameter and feed factories and artifact writers cannot resolve scoped services.
- Kiji's own services, such as `SiteInfo`, `PageInfo`, and `ContentCollection<T>`, cannot
  be replaced, and Kiji's hosted service cannot be removed. Replacing the container with
  `ConfigureContainer` is not supported.
- Page generation and the development server start after every hosted service has started.
  Hosted service state is not tracked for HTML reuse; use `AlwaysRender` on components
  that read it.

### Environment

`Environment` is a standard
[`IHostEnvironment`](https://learn.microsoft.com/dotnet/core/extensions/generic-host#ihostenvironment),
also injectable into components:

```csharp
if (builder.Environment.IsDevelopment())
{
    // For example, also list draft posts while serving.
}
```

`dotnet publish` uses `DOTNET_ENVIRONMENT` and defaults to `Production`. `dotnet watch`
defaults to `Development`. While serving, precedence is `--environment`, then
`DOTNET_ENVIRONMENT`, then `ASPNETCORE_ENVIRONMENT`. Do not change the environment name
or content root after `CreateBuilder`; `Build` rejects it because the configuration files
and paths are already selected.

### Configuration

`Configuration` is a standard
[configuration](https://learn.microsoft.com/dotnet/core/extensions/configuration), also
injectable as `IConfiguration`. It reads `appsettings.json` and then
`appsettings.{Environment}.json` from the project directory. See
[Configuration](../configuration/) for a walkthrough.

```csharp
builder.Services.Configure<ImageProcessorOptions>(builder.Configuration.GetSection("Images"));
```

- Environment variables, command-line arguments, and user secrets are not read. Add a
  source when you need one, such as `builder.Configuration.AddEnvironmentVariables("MYSITE_")`.
- Restart `dotnet watch` after editing the files.
- Changing only the `Logging` or `Metrics` section does not regenerate pages on publish.
- The development server answers only `localhost`, `127.0.0.1`, and `[::1]`. To use
  another hostname, add it to `AllowedHosts` and keep the local entries, for example
  `"AllowedHosts": "localhost;127.0.0.1;[::1];dev.example"` in `appsettings.Development.json`.

The development server reads its own standard ASP.NET Core configuration, including
project settings files, environment variables, and command-line arguments. Its `kiji`
status lines support these settings:

| Key | Effect |
| --- | --- |
| `DOTNET_WATCH_SUPPRESS_EMOJIS` | `1` or `true` replaces emoji markers with text markers. |
| `NO_COLOR` | `1` or `true` disables ANSI colors. |
| `TERM` | `dumb` disables ANSI colors (case-insensitive). |

Colors are also disabled when output is redirected. These settings affect only Kiji's
status lines, not `dotnet watch` output or `ILogger` formatting. For example, add
`"DOTNET_WATCH_SUPPRESS_EMOJIS": true` to `appsettings.Development.json` to use text markers
while developing. Restart the server after changing the file.

Prefer environment variables or development-only settings for terminal preferences.
If these keys are included in the configuration used by publish, changing them
invalidates page reuse, just like other settings outside `Logging` and `Metrics`.

### Logging

`Logging` is a standard
[`ILoggingBuilder`](https://learn.microsoft.com/dotnet/core/extensions/logging), configured
by the `Logging` section as in other .NET hosts. The console shows warnings and errors,
including exceptions caught by an
[`ErrorBoundary`](https://learn.microsoft.com/aspnet/core/blazor/fundamentals/handle-errors?view=aspnetcore-10.0#error-boundaries),
and Kiji's summary. `Debug` lists each generated file:

```json
{ "Logging": { "LogLevel": { "Kiji.Build": "Debug" } } }
```

- Logs never fail a publish. To stop a publish, throw an exception that no error boundary
  catches.
- In a terminal, `dotnet publish` shows Kiji's messages only with `-v d`, which also shows
  why a publish failed. CI logs always show them.
- Pages reused from an earlier publish do not log their messages again.

### Page parameters

`AddPageParameters<TPage>` supplies the values for a component's routes. See
[Routing](../routing/) for examples.

- Each set is an anonymous object or a dictionary with string keys. Names match
  `[Parameter]` properties, ignoring case, and values must already have the property's type.
- Each set is rendered at every parameterized route of the component. A component with only
  fixed routes takes at most one set, which replaces its default values.
- Omitted parameters keep their defaults; `EditorRequired` is not checked.
- Unknown names, wrong types, missing route values, and duplicate output paths fail before
  rendering.
- Calls for the same component add up; an empty sequence generates no pages.

A page's HTML is reused across publishes only when every value is `null`, a `string`,
`bool`, `char`, an integer, an enum, a `Guid`, or an item from a `ContentCollection<T>`.
The same item can go to several pages, for example as `Post` on one page and `Previous`
on the next. Other values, such as arrays or objects created in the factory, make the
page render on every publish.

## `SiteInfo`

Site metadata, set with `builder.UseSiteInfo(...)` and injectable into components.

| Property             | Default  | Behavior                                                                                             |
| -------------------- | -------- | ---------------------------------------------------------------------------------------------------- |
| `Uri BaseUrl`        | Required | The site's public URL, such as `https://example.com/blog/`. Its path is the base path of every page. |
| `string Name`        | Required | Site name, also the RSS channel title.                                                               |
| `string Description` | `""`     | Site description, also the RSS channel description.                                                  |
| `string Language`    | `"en"`   | Written to `<html lang>` and the RSS channel.                                                        |
| `string Author`      | `""`     | For your components; Kiji does not write it.                                                         |

`Uri ResolveUrl(string path)` returns the absolute URL of a site-relative path. A leading
`/` is ignored, so `"about/"` and `"/about/"` both stay under the base path, and `""`
returns `BaseUrl`. A query or fragment may follow a path, as in `"about/?lang=ja#team"`, but
cannot stand alone: `"?page=2"` and `"#top"` throw `ArgumentException`.

## `PageInfo`

Describes the page being rendered. Inject it in pages, layouts, and components.

| Property  | Behavior                                                                                                    |
| --------- | ----------------------------------------------------------------------------------------------------------- |
| `Uri Url` | The page's absolute public URL, including the base path. The development server uses the published URL too. |

```razor
@inject PageInfo Page

<HeadContent>
    <link rel="canonical" href="@Page.Url.AbsoluteUri" />
</HeadContent>
```

## `ContentCollection<T>`

The items of one content source, injected as a service. Markdown IDs are paths relative to
the content directory, such as `posts/hello/index.md`, and ignore case. For how reads affect
page reuse, see [what changes cause work](../incremental-builds/#what-changes-cause-work).

| Member                                     | Behavior                                                   |
| ------------------------------------------ | ---------------------------------------------------------- |
| `int Count`                                | Number of items.                                           |
| `GetEnumerator()`                          | Enumerates items in path order.                            |
| `T this[string id]`                        | Looks up an item; throws `KeyNotFoundException` if absent. |
| `bool TryGetValue(string id, out T? item)` | Looks up an item, or returns `false`.                      |
| `bool Contains(string id)`                 | Tests whether the item exists.                             |

## `SiteOutputContext`

Passed to the writer registered with `AddArtifact`.

| Property                            | Behavior                                      |
| ----------------------------------- | --------------------------------------------- |
| `SiteInfo Site`                     | The site metadata.                            |
| `IReadOnlyList<SitePageInfo> Pages` | Every generated page.                         |
| `IServiceProvider Services`         | Site services, including content collections. |

## `SitePageInfo`

A generated page, listed in `SiteOutputContext.Pages`.

| Property                    | Behavior                                                                               |
| --------------------------- | -------------------------------------------------------------------------------------- |
| `string RelativePath`       | The page URL relative to `BaseUrl`, such as `posts/hello/`, or `""` for the home page. |
| `string OutputRelativePath` | The output file, such as `posts/hello/index.html`, with `\` separators on Windows.     |

## `AlwaysRenderAttribute`

`[AlwaysRender]` renders every page that contains the component, including through a
layout, on every publish. Derived components inherit it. See
[external data](../incremental-builds/#render-pages-that-use-external-data).
