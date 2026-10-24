# Package tests

Run `dotnet test -c Release` here for package tests, or at the repository root
for both test projects. `Kiji.Tests` checks library behavior without building
consumer projects. `Kiji.PackageTests` checks the NuGet package through `dotnet`
and HTTP on Windows and Linux.

| Scenario | What it checks | Why a package test? |
| --- | --- | --- |
| Publish, edit, clean | Razor/Markdown/images, library assets, updated output, `KijiForce`, service-worker hashes, uncompressed default and opt-in compressed HTML, boundary warnings and cleanup | Exercises the shipped MSBuild targets, console output and native dependencies in a real consumer. |
| Watch and reload (Razor/Web SDKs) | Markdown edits, additions and deletions under the documented `content/**` exclusion update article lists/routes; JS, Razor and scoped CSS edits reach the browser with one reload | Requires the SDK's `dotnet watch` integration and generated asset manifests; content outside the SDK's items must still reload exactly once. |
| Windows cleanup | Cleanup waits for a held file lock to be released | Covers the workspace cleanup used after stopping the consumer's process tree. |

`PackageFixture` packs Kiji once (or uses `KIJI_TEST_PACKAGE` from CI), prepares
the consumer and cleans up. `Fixture/` holds its source template.
`RunningCommand` runs `dotnet`, includes a bounded log tail in failures and
stops the processes it started.

All scenarios run serially in `artifacts/package-tests/work`. A file lock also
prevents separate test runs from using it at the same time. Work, the local feed
and the private Kiji package are removed after success or failure; the next run
cleans up an interrupted run. Only the empty lock file remains.
On Windows, cleanup retries access-denied, sharing/lock violations and
directory-not-empty errors for up to ten seconds: waiting for the `dotnet`
parent to exit does not guarantee its children have released their executables.
Persistent failures still fail the test with the deletion exception.

SkiaSharp and other dependencies use the shared NuGet global cache. Only Kiji is
installed privately so the selected package is tested even if another copy of
the same version is cached. CopyLocal requests hard links where available;
ordinary copies are allowed and are removed with the workspace. CI puts the
shared cache on the checkout volume to allow hard links. Cleanup never clears
the shared cache.

Add detailed behavior tests to `Kiji.Tests`; extend these scenarios only for
regressions that require the package or SDK boundary.
