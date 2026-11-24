---
name: measure-performance
description: Measure Kiji build performance and validate an optimization. Use when changing anything on the build hot path, when asked to make something faster, or before writing any performance number into prose.
---

## Choose the measurement

| Harness | Use for |
| --- | --- |
| BenchmarkDotNet (`benchmarks/Kiji.Benchmarks`) | Implementation comparisons. `SiteBuildBenchmarks` is the authoritative whole-build result (200/1000 pages; Full, NoChange, CacheOnly, OneEdited, CodeChanged). `ImageProcessingBenchmarks` and `ContentLoadBenchmarks` isolate images and content loading. |
| `benchmarks/Kiji.SyntheticSite --phases` | Attributing time to build phases with saved binaries. Its absolute times are not comparable with BDN. |
| `scripts/Measure-ColdPublish.ps1` | What a user's `dotnet publish` runs. Most of that time is first use of code, which the warm, repeated builds above do not show. |
| `scripts/Measure-Publish.ps1` | Whole-publish regressions through real Razor SDK consumers, including SDK work outside Kiji. |
| `scripts/Measure-DevServer.ps1` | Dev server startup, first response and edit-to-reload. |

```pwsh
dotnet run -c Release --project benchmarks/Kiji.Benchmarks -- --filter "*SiteBuildBenchmarks*"
dotnet run -c Release --project benchmarks/Kiji.SyntheticSite -- --pages 1000 --runs 5 --full --phases --root <dir> --out <json>
dotnet pwsh .agents/skills/measure-performance/scripts/Measure-ColdPublish.ps1 -Baseline <revision> -Work <new directory> [-Phases]
dotnet pwsh .agents/skills/measure-performance/scripts/Measure-Publish.ps1 -BaselinePackage <nupkg> -CandidatePackage <nupkg> -CorpusWriter <saved Kiji.SyntheticSite.dll>
dotnet pwsh .agents/skills/measure-performance/scripts/Measure-DevServer.ps1 -BaselinePackage <nupkg> -CandidatePackage <nupkg> -CorpusWriter <saved Kiji.SyntheticSite.dll> -OutputDirectory <new directory>
```

Keep maintained benchmarks on current production paths; temporary comparisons and rejected
experiments go in `artifacts/`. `.github/workflows/benchmarks.yml` runs weekly and on demand;
there is no automatic performance gate.

## Comparing builds

- The baseline is saved Release binaries or packages from the actual starting state,
  including uncommitted changes. A project reference to the working tree is not a baseline;
  for BDN, load both versions through equivalent AssemblyLoadContexts.
- Use one corpus (`--root`) for both. The SyntheticSite definition is frozen; integration
  fixtures belong in tests.
- SyntheticSite has no warmup: discard run 1 of each invocation, and extend warmup equally if
  times keep falling. Without `--full`, runs after the first are no-change builds; `--full`
  clears `.kiji` each run; `--images` adds a cover every tenth post.
- Phase names with a dot are nested inside their parent: sum only names without one. The
  remainder includes site construction.
- Output must stay identical; do not normalize differences unless the task allows it.

## Cold publish

- Compare packages packed with the default `KijiReadyToRun`, with the `Kiji.Runtime.*`
  packages in the same feed: a package consumer runs ReadyToRun images, a project reference
  runs IL, and the two are not comparable.
- Report two states. Fresh: no `.kiji`, staged `site` or `publish.jitprofile*`, as in a clean
  checkout. Profiled: remove only `.kiji/cache` and the staged `site`, and discard the first
  two runs, which record the profile.
- `-Phases` adds a startup hook that itself costs time: use it to attribute, not for headline
  numbers. To measure Windows and macOS, push a branch with a temporary CI step.
- File systems differ far more than Kiji's own time: compare processor time on a memory file
  system and repeat on a disk for writes.
- By hand, time the command `KijiGenerateSite` runs:
  `dotnet exec --runtimeconfig obj/Release/net10.0/<rid>/kiji/publish.runtimeconfig.json bin/Release/net10.0/<rid>/<Site>.dll`.

## Already measured for the cold publish

Rejected: collecting code identity while the host is built (contends with the host's own
loading), warming the component renderer (no difference), skipping the Markdown warm-up while
a JIT profile replays (slower). Partial ReadyToRun images from a MIBC profile need dotnet-pgo,
which nuget.org does not carry. Rejected runtime settings and APIs report their measurements
from `PublishRuntimeSettingsTests` and `src/BannedSymbols.txt`.

## Reading Measure-Publish results

- ManifestRemoved (only `.kiji/cache/manifest.json` deleted) replaces the older ForceRender;
  re-run both packages under it to compare with a ForceRender baseline.
- PublishOutputRemoved removes only `dist` and is not Kiji's CacheOnly scenario.
- Fresh restores outside timing with warm OS and NuGet caches; it is not a cold machine.
- MSBuild categories are exclusive wall time and add up to the mean publish time; do not add
  inclusive task/target totals or dotted Kiji phases to them, and do not subtract the
  measured instrumentation overhead from phase timings.
- Unexpected compiler invocations mean unwanted recompilation; diagnose with a separate
  build instead of changing consumer settings.
- Older packages compress by default and current ones opt in: set the same
  `CompressionEnabled` in both property sets. The changed default is not an optimization.
  `AllowCompressionDifferences` and `AllowNewFingerprintAliases` are only for deliberate
  representation changes against versions without them.

## Reading Measure-DevServer results

Kiji warms its content snapshot in the background, so first-request time depends on when the
request arrives: do not add independent medians of startup and first response. Edit-to-visible
time includes the 250 ms debounce and measures HTTP completion, not browser paint. Do not
infer a slowdown from percentages on submillisecond requests.
