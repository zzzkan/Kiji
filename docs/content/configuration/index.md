---
title: Configuration
description: Read settings from appsettings.json, vary them by environment, and control logging.
order: 45
section: Guides
---

A Kiji site uses the standard .NET
[configuration](https://learn.microsoft.com/dotnet/core/extensions/configuration),
[options](https://learn.microsoft.com/dotnet/core/extensions/options), and
[logging](https://learn.microsoft.com/dotnet/core/extensions/logging) APIs through the
builder that `StaticSite.CreateBuilder` returns.

## Settings files

Kiji reads `appsettings.json` and then `appsettings.{Environment}.json` from the project
directory. Later files override earlier ones key by key:

```json
{
  "Site": {
    "RepositoryUrl": "https://github.com/example/my-site",
    "PostsPerPage": 10
  }
}
```

The files are read while building the site and are not copied to the published output.
Restart `dotnet watch` after editing them.

## Read settings

Bind a section to a class and inject it where you need it:

```csharp
namespace MySite.Models;

public sealed class SiteSettings
{
    public string RepositoryUrl { get; set; } = string.Empty;
    public int PostsPerPage { get; set; } = 10;
}
```

```csharp
builder.Services.Configure<SiteSettings>(builder.Configuration.GetSection("Site"));
```

```razor
@using Microsoft.Extensions.Options
@inject IOptions<SiteSettings> Settings

<a href="@Settings.Value.RepositoryUrl">Source</a>
```

Factories receive the same services, so page parameters can use settings too:

```csharp
var pageSize = services.GetRequiredService<IOptions<SiteSettings>>().Value.PostsPerPage;
```

For a single value, inject `IConfiguration` and read `Configuration["Site:RepositoryUrl"]`.
Kiji's own options bind the same way; see
[`ImageProcessorOptions`](../api-images/#imageprocessoroptions).

## Environments

The environment name selects the second settings file and is available as
`builder.Environment` and as an injectable `IHostEnvironment`:

| Command          | Default environment | How to change it                                                              |
| ---------------- | ------------------- | ----------------------------------------------------------------------------- |
| `dotnet watch`   | `Development`       | `--environment`, then `DOTNET_ENVIRONMENT`, then `ASPNETCORE_ENVIRONMENT`     |
| `dotnet publish` | `Production`        | `DOTNET_ENVIRONMENT`                                                          |

For example, `appsettings.Development.json` can override a value while you write:

```json
{ "Site": { "PostsPerPage": 3 } }
```

Use the name in `Program.cs` for behavior that differs between serving and publishing:

```csharp
if (builder.Environment.IsDevelopment())
{
    // For example, also list draft posts while serving.
}
```

See [Environment](../api-kiji/#environment) for the rules.

## Other sources

Environment variables, command-line arguments, and user secrets are not read by default,
so a publish gives the same result on every machine. Add a source when you need one:

```csharp
builder.Configuration.AddEnvironmentVariables("MYSITE_");
```

`MYSITE_Site__RepositoryUrl` then overrides `Site:RepositoryUrl`. The published site is
public, so do not render secrets into pages.

## Logging

The `Logging` section sets log levels as in other .NET hosts. The console shows warnings,
errors, and Kiji's summary; `Debug` for `Kiji.Build` lists each generated file:

```json
{ "Logging": { "LogLevel": { "Kiji.Build": "Debug" } } }
```

A `Default` level applies to `Kiji.Build` too, so `"Default": "Warning"` hides the summary.

In a terminal, `dotnet publish` shows these messages only with `-v d`. Inject
`ILogger<T>` to log from your own components and services. Logs never fail a publish; see
[Logging](../api-kiji/#logging).

## Settings and incremental builds

Kiji does not track which keys a page reads. Changing the environment name or any setting
outside the `Logging` and `Metrics` sections renders every page on the next publish. See
[Incremental builds](../incremental-builds/#what-changes-cause-work).

## Development server

The development server reads its own standard ASP.NET Core configuration, including
`--urls` and `AllowedHosts`. See [Configuration](../api-kiji/#configuration) in the API
reference.
