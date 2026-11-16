---
title: Contributing
description: Report issues, improve these docs, and build Kiji from source.
order: 80
section: Help
---

Contributions to Kiji and its documentation are welcome. Kiji is developed at
[github.com/zzzkan/kiji](https://github.com/zzzkan/kiji) under the
[MIT License](https://github.com/zzzkan/kiji/blob/main/LICENSE).

## Report an issue

[Open an issue](https://github.com/zzzkan/kiji/issues/new/choose) and choose the form that
fits: a bug report, a feature request, or a documentation issue. Check
[Troubleshooting and FAQ](../troubleshooting/) first for known problems.

## Improve the documentation

Every page has an **Edit this page on GitHub** link at the bottom. The pages are Markdown
files under `docs/content/`, and the site itself is a Kiji project in `docs/`. To preview
your changes, clone the repository and run:

```powershell
dotnet watch --project docs
```

## Build and test

Building needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). From
the repository root:

```powershell
dotnet build -c Release
dotnet test -c Release
```

The library is in `src/Kiji`, its unit and component tests are in `src/Kiji.Tests`, and
`src/Kiji.PackageTests` tests the packed package from a consumer project.

## Open a pull request

Describe what changed and why, link the related issue, list the checks you ran, and note
any effect on existing behavior or APIs. The pull request template has a section for each.
