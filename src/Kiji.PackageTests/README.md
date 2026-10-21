# Package tests

Run `dotnet test -c Release` here for package tests, or at the repository root
for both test projects. `Kiji.Tests` checks library behavior without building
consumer projects. `Kiji.PackageTests` checks the NuGet package through `dotnet`
and HTTP on Windows and Linux.

| Scenario | What it checks | Why a package test? |
| --- | --- | --- |
| Publish, edit, clean | Razor/Markdown/images, library assets, updated output, `KijiForce`, service-worker hashes, compressed HTML and cleanup | Exercises the shipped MSBuild targets and native dependencies in a real consumer. |
| Console logging | Warning and higher logs, including handled `ErrorBoundary` exceptions, appear during ordinary publish | MSBuild's output filtering must retain diagnostics without failing a successful render. |
| Watch and reload (Razor/Web SDKs) | JS, Razor and scoped CSS edits reach the browser with one reload | Requires the SDK's `dotnet watch` integration and generated asset manifests. |

`PackageFixture` packs Kiji once (or uses `KIJI_TEST_PACKAGE` from CI), prepares
the consumer and cleans up. `Fixture/` holds its source template.
`RunningCommand` runs `dotnet`, includes a bounded log tail in failures and
stops the processes it started.

All scenarios run serially in `artifacts/package-tests/work`. A file lock also
prevents separate test runs from using it at the same time. Work, the local feed
and the private Kiji package are removed after success or failure; the next run
cleans up an interrupted run. Only the empty lock file remains.

SkiaSharp and other dependencies use the shared NuGet global cache. Only Kiji is
installed privately so the selected package is tested even if another copy of
the same version is cached. CopyLocal requests hard links where available;
ordinary copies are allowed and are removed with the workspace. CI puts the
shared cache on the checkout volume to allow hard links. Cleanup never clears
the shared cache.

Add detailed behavior tests to `Kiji.Tests`; extend these scenarios only for
regressions that require the package or SDK boundary.
