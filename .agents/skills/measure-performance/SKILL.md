---
name: measure-performance
description: Measure Kiji build performance and validate an optimization. Use when changing anything on the build hot path, when asked to make something faster, or before writing any performance number into prose.
---

## Choose the measurement

- **BenchmarkDotNet**: compare implementations with an explicit baseline, warmup,
  allocation measurements, and output equivalence checked in setup.
- **SiteBuildBenchmarks**: authoritative whole-build results for 200/1000 pages and
  Full, NoChange, CacheOnly, OneEdited, CodeChanged scenarios.
- **SyntheticSite --phases**: attribute changes to build stages and compare saved
  binaries in interleaved runs. Its absolute times are not comparable with BDN.

```pwsh
dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*SiteBuildBenchmarks*"
dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*Markdown*"
dotnet run -c Release --project src/Kiji.SyntheticSite -- --pages 1000 --runs 5 --full --phases --root <dir> --out <json>
```

ImageProcessingBenchmarks covers shared/distinct images, cold/warm output and
ordinary/high resolution. ContentLoadBenchmarks separates file I/O from parsing.
Keep maintained benchmarks on current production paths; temporary comparisons and
rejected experiments belong in ignored `artifacts`.

## Before/after protocol

1. Set an acceptance threshold before measuring. Save Release baseline binaries
   from the actual starting state, including uncommitted changes.
2. Generate one corpus and use the same `--root` for both binaries. Keep the
   SyntheticSite site definition frozen; integration fixtures belong in Tests.
3. Alternate baseline/candidate for at least three rounds, `--runs 5 --full`.
   Discard run 1 of **each invocation** for JIT warmup; compare remaining medians.
4. Confirm with SiteBuildBenchmarks. For a BDN saved-binary comparison, load both
   versions through equivalent AssemblyLoadContexts and verify identical output
   in setup. A project reference to the working tree is not a saved baseline.
5. Record commands, CPU/core count, .NET version, GC mode and output equivalence.
   Attribute differences with `--phases`; sum only names without a dot, since
   nested phases overlap their parents. The remainder includes site construction.

Filesystem/virus-scanner variation makes a single before-then-after pair unreliable.
Do not normalize output differences unless the task explicitly permits them.

## Harness details

- SyntheticSite has no warmup. Without `--full`, runs after the first are no-change
  builds. `--full` clears `.kiji` each run; `--images` adds a cover every tenth post.
- BDN creates and removes corpora outside measurement. Alternate equal-length
  edits in iteration setup; do not accumulate edits across iterations.
- Server GC is off by default; report any override.
- `.github/workflows/benchmarks.yml` runs manually and weekly, outside blocking CI.
  There is no automatic performance gate.

## Attribute whole-publish regressions

Use `scripts/Measure-Publish.ps1` to compare saved packages through real isolated
Razor SDK consumers, including SDK work outside Kiji's generator:

```pwsh
./.agents/skills/measure-performance/scripts/Measure-Publish.ps1 `
  -BaselinePackage <saved-baseline.nupkg> -CandidatePackage <candidate.nupkg> `
  -CorpusWriter <saved-Kiji.SyntheticSite.dll> -Pages 1000 -Rounds 5
```

The script copies the frozen site definition and one corpus into both consumers.
Only the entry adapter changes from direct PublishAsync to package RunAsync and
installs the existing phase observer. Package and workload hashes are recorded.
The measurement-only MSBuild logger compiles under the ignored run directory;
it is not a Kiji dependency, solution project or packaged build task.

- NoChange retains all outputs. ForceRender uses KijiForce without clearing files.
  OneEdited alternates equal-length edits to one Markdown input.
- PublishOutputRemoved removes only dist, retaining Kiji staging and caches. Do not
  call this Kiji's CacheOnly scenario: that benchmark repairs the generator output.
- Fresh clears bin, obj, .kiji and dist before every invocation, then restores outside
  timing. OS and NuGet package caches remain warm; this is not a cold machine test.
- Each scenario discards one warmup pair and alternates at least three measured pairs.
  All common public bytes and every compressed representation are verified per pair.
- MSBuild events provide exclusive wall-time categories, including inline item-list
  work. Do not add inclusive task/target totals or dotted Kiji sub-phases to them.
  Category means add to mean whole-publish time; independent medians need not add up.
- Kiji entry timing covers construction, RunAsync and disposal. Exec also includes
  process startup/shutdown and report writing. Keep these distinct from warm BDN.
- File counts and SDK manifest counts show added work. Reported write counts infer
  new/updated files from size/mtime; they are not OS-level I/O tracing.
- The on/off control measures instrumentation overhead with the same compiled
  adapter. Do not silently subtract that estimate from reported phase timings.
- Compiler invocation counts reveal unwanted recompilation. Diagnose unexpected
  calls with a separate diagnostic build; do not alter consumer settings just to
  make the comparison faster or conceal production behavior.
- Use `BaselineProperties` and `CandidateProperties` for explicit MSBuild option
  comparisons; these apply to restore, publish and instrumentation controls across
  the project graph. Record the settings with package provenance. Opt into
  `AllowCompressionDifferences` only for deliberate representation changes: plain
  file sets/bytes must remain identical and retained sidecars must decode correctly.
  Report physical output storage separately from HTTP transfer sizes; sidecars
  increase disk usage but reduce transfer only when the host negotiates them.
- When comparing against a version without fingerprint URL materialization, use
  `AllowNewFingerprintAliases` explicitly. Added ordinary files must correspond
  to SDK endpoints carrying both a label and fingerprint and must match that
  label's baseline bytes; arbitrary extra outputs still fail. Keep this separate
  from permission to add or remove compressed representations.

Preserve the run directory (raw spans, per-publish Kiji phases, results, output
checks, package identities and harness snapshot). Run one measurement at a time.

## Measure the development loop

Use `scripts/Measure-DevServer.ps1` with saved `BaselinePackage`, `CandidatePackage`,
`CorpusWriter`, and a new `OutputDirectory`. It adapts the same frozen site to
RunAsync and measures 200/1000 pages with the ordinary Debug configuration.

- Direct runs use a built DLL; WatchWarm includes watch/MSBuild with existing build
  outputs; WatchFresh removes bin/obj/.kiji, restores outside timing, then runs watch.
- Separate process-to-listening, first complete HTTP response, and their total.
  Kiji warms its content snapshot in the background, so first-request time depends
  on when the request arrives; do not add independent medians.
- Keep one warmup process pair outside the result and alternate five measured
  pairs. Discard the first five requests per endpoint and first edit per type.
  Aggregate repeated requests within each process before comparing processes.
- Verify response bytes between variants in each mode. Content/CSS edits wait for
  the real WebSocket reload and assert updated response bytes; include the 250ms
  debounce in edit-to-visible time. This measures HTTP completion, not browser paint.
- Record medians, ranges and request p95. Do not infer a meaningful slowdown from
  percentages on submillisecond requests alone. Keep raw samples and process logs.
- All owned process trees stop in finally blocks; no global environment changes.
  No other tests or benchmarks may run concurrently with the measurements.
