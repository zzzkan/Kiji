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
