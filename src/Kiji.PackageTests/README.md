# Package scenarios

`Kiji.Tests` exercises the library directly, including component rendering and
in-process hosting. This project exercises a real NuGet consumer through `dotnet`
commands, generated files and HTTP. The runner has a build-only dependency on
Kiji; it does not reference its API or carry its runtime dependencies.

Run `dotnet test -c Release` in this directory, or run the same command at the
repository root to run both test projects. No extra test-runner flags are needed.
The build-only dependency builds Kiji before the fixture packs it once, with
normal package/baseline validation enabled. CI instead sets `KIJI_TEST_PACKAGE`
to the absolute path of the exact nupkg from its preceding pack step. Release
publishes that same artifact after these tests pass.

## Scope

The checked-in `Fixture` is source material for **one** consumer site, plus its
small Razor and C# libraries. It is not part of the main solution and is never
built or edited in place. Three workflows replace independent per-feature sites:

- Publish, reuse, edit and republish: Razor/Markdown, real SkiaSharp image output,
  RCL resources/scoped CSS, fingerprints, AlwaysRender/KijiForce, service workers
  and compression transitions. Head content, navigation, JS interop, sitemap and
  routing semantics stay in the library tests rather than expanding this consumer.
- Build from outside the project, use an alternate output directory, publish,
  serve, watch and clean; finally exercise a consumer with no static assets.
- Develop and reload: run once with the Razor SDK and once with the Web SDK,
  covering both build-compression settings without multiplying every scenario.
  Check live HTTP validators/ranges, Razor/CSS updates and reload ownership.

Each scenario starts from source and disposes its own servers. Tests do not
depend on execution order. The original fixture files remain unchanged.

## Disk and lifetime rules

`artifacts/package-tests` has a fixed layout:

| Path | Lifetime |
| --- | --- |
| `.lock` | A file lease serializes separate test processes in this checkout. |
| `work` | One active site; removed before and after every scenario. |
| `feed` | One selected package; removed after the suite. |
| `packages` | Only the tested Kiji package; removed after the suite. |

SkiaSharp and other external dependencies use the normal NuGet global cache,
located through `dotnet nuget locals global-packages --list`. This respects
`NUGET_PACKAGES` and NuGet configuration instead of assuming a user-profile path.
A restore-only Web SDK project reads dependency versions from the selected Kiji
nuspec and fills any missing dependencies, including implicit SDK packages, in
that global cache. It does not compile a second site or copy native binaries.
CI and release set `NUGET_PACKAGES` to `artifacts/nuget-packages` in the checkout
for the whole job. This keeps the cache on the checkout volume, including Windows
runners whose user profile and checkout are on different drives. It is the same
cache for the solution build and every package scenario, not a per-test restore.

[PackageDownload](https://learn.microsoft.com/nuget/consume-packages/packagedownload-functionality)
installs just the selected Kiji package in `packages`, without
its dependencies or any fallback cache. Consumers then use this private cache
first and the global cache as a fallback. Even a same-version Kiji in the global
cache cannot replace the package under test. Tests compare the private nupkg and
the consumer's Kiji assemblies byte for byte with the selected package, and
require that `packages` contains only Kiji. The consumer feed contains only Kiji,
so missing external dependencies cannot silently be restored
privately. Package installation uses NuGet's normal extraction and validation.

Only the empty lease file remains under `artifacts/package-tests` after a run.
Global dependencies stay available to later tests and ordinary builds; fixture
cleanup never clears, deletes or modifies the user's global cache.

SDK CopyLocal uses hard links, including SkiaSharp's native binaries and PDBs.
The global cache and workspace must be on the same volume. Tests use the resolved
package versions from `project.assets.json` and check Windows file IDs or
Linux device/inode IDs for every emitted SkiaSharp native file; a silent fallback
to independent copies fails verification. The filesystem must support hard links
(NTFS on Windows; normal Linux CI filesystems). Summing file lengths double-counts
hard links and is not a physical disk-usage measurement.

The consumer props explicitly use `BaseIntermediateOutputPath="obj/"` for all
fixture projects. SDK 10.0.402's watch tooling reads the static-asset manifest path
without normalizing the default `obj\` separator on Unix; using a forward slash
keeps scoped CSS hot reload working on both platforms without changing the layout.

Success, assertion failure and command timeout all delete the workspace, feed
and private Kiji cache. A forced termination can leave at most this one
set of files; the next lease owner removes it before starting. The lease file
itself is intentionally not deleted, avoiding lock-file replacement races.
Command/server output is a bounded in-memory tail
included in failures, not a growing collection of log files. No browser is launched.

Share external dependencies through the normal global NuGet cache across runs.
Do not add per-test sites, GUID run directories, retained build trees or caches,
source `ProjectReference`s to the consumer, or per-feature pack operations.
Add detailed cases to `Kiji.Tests` first and
extend a package workflow only when the SDK/package boundary matters.
