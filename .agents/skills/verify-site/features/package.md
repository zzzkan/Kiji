# Package consumption

## Sub-features

- A consumer that references the `Kiji` package restores `Kiji.Runtime.<rid>` and
  SkiaSharp's native assets for its runtime identifier.
- Publish runs Kiji's ReadyToRun images instead of IL when package id and version match.
- `dotnet watch` and `dotnet publish` work through the shipped `Kiji.targets`.

## How to get to it (user POV)

The author runs `dotnet add package Kiji` (or uses the `kiji-blog` template) and publishes.

## Driving it with Kiji.PackageTests

```sh
cd tests/Kiji.PackageTests && dotnet test -c Release
```

This packs Kiji, creates one isolated consumer and drives publish, edit, clean, watch and
reload through `dotnet` and HTTP. See `tests/Kiji.PackageTests/README.md` for its workspace
and cleanup. For timing through the package, use the measure-performance skill's
`Measure-ColdPublish.ps1`.

## Gotchas

- `docs/` uses a project reference and runs IL, so it cannot show ReadyToRun or
  restore behavior.
- Packing compiles images against each runtime pack, so the first pack downloads those packs.
- The watch scenario is skipped on macOS (see the package tests' README).
