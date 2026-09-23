---
name: measure-performance
description: Measure Kiji build performance and validate an optimization. Use when changing anything on the build hot path, when asked to make something faster, or before writing any performance number into prose.
---

## Which harness answers which question

| Question                                                | Use                                                                                                   | Why                                                                                                                                                     |
| ------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Is this implementation faster than the one it replaces? | **BenchmarkDotNet** — put the old implementation beside the new one as `[Benchmark(Baseline = true)]` | Warmup, iteration counts, confidence intervals and allocations; cases normally run in separate benchmark processes                                      |
| Did the whole build get faster?                         | **`SiteBuildBenchmarks`** (BenchmarkDotNet, `RunStrategy.Monitoring`)                                 | Same statistics, applied to a real build. This is the authoritative end-to-end number                                                                   |
| Which phase moved?                                      | **`Kiji.SyntheticSite --phases`**                                                                     | BenchmarkDotNet measures methods. Only the phase observer attributes time to snapshot / plan / clean / static / render / artifacts / entries / manifest |
| Is this commit faster than that commit?                 | **`Kiji.SyntheticSite`, A/B interleaved**                                                             | Alternate two saved binaries on one corpus; a dedicated AssemblyLoadContext also allows an explicit BDN baseline adapter                                |

**Adopt or reject with BenchmarkDotNet. Confirm with the end-to-end job. Explain with
`--phases`.** A total that moved is not an explanation until a phase can be pinned on it.

```pwsh
# End-to-end, all scenarios and sizes. The authoritative number.
dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*SiteBuildBenchmarks*"

# One hot path in isolation.
dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*Markdown*"

# Phase attribution, and cross-commit A/B.
dotnet run -c Release --project src/Kiji.SyntheticSite -- --pages 1000 --runs 5 --full --phases --root <dir>
```

`SiteBuildBenchmarks` covers 200 / 1000 pages × `Full` / `NoChange` / `CacheOnly` /
`OneEdited` / `CodeChanged`. Each case generates a unique deterministic corpus in
`GlobalSetup` and deletes it in `GlobalCleanup`, outside measurement. Edits alternate
between equal-length bodies in `IterationSetup`; never append repeatedly.

`ImageProcessingBenchmarks` measures the current image pipeline with a separate generated
workload (shared/distinct, cold/warm, ordinary/high-resolution). `ContentLoadBenchmarks`
separates file reads from actual Markdown loading. Benchmark current production paths;
keep temporary comparison implementations and rejected experiments under ignored
`artifacts`, not in the maintained benchmark suite.

`SyntheticSite` flags: `--pages N`, `--runs N`, `--images` (adds a cover image to every
tenth post), `--full` (delete `.kiji` before every run, so every run is a full build),
`--phases` (per-stage breakdown), `--root <dir>` (reuse a generated site), `--out <json>`.

## Comparing harnesses

Quote `SiteBuildBenchmarks` for end-to-end results. Use `SyntheticSite` for phase
changes relative to itself; do not compare absolute times from different harnesses.

## Using SyntheticSite by hand

It has no warmup, so its output needs reading with care:

- **Discard run 1.** It includes JIT warm-up.
- **Without `--full`, runs 2..N are no-change builds.** A median over them is not a
  full-build number. Pass `--full` when comparing full builds.
- **Sum only top-level phases (names without a dot).** Child phases overlap their parent; never add both to the total. Check this sum against the run total. The remainder is `StaticSite`
  construction and assembly scanning.

## Comparing two commits

The write phase is dominated by the filesystem and whatever scans it — it swings by
hundreds of milliseconds between sessions on the same binary. **Only an interleaved
comparison within a single session is valid.**

1. Preserve the requested starting state, including uncommitted changes. Save the baseline binaries before editing; do not substitute HEAD for a dirty baseline. Build both in Release.
   A BenchmarkDotNet comparison of saved binaries must load both through equivalent
   AssemblyLoadContexts and verify equal output in setup. A project reference to the
   current tree is not a saved baseline. Do not mix default-context and saved-context
   timings or normalize output differences unless the change explicitly permits them.
2. Generate the corpus once and share it: both sides run with the same `--root`.
3. Alternate baseline / candidate for at least three rounds, `--runs 5 --full`.
4. Drop run 1 of every invocation; compare medians of what is left, per phase.
5. Report the delta with the commands and the environment (CPU, core count, .NET
   version, GC mode) beside it.

A single before-then-after pair cannot separate a code change from session variation.

## Traps

- **Do not compare numbers across harnesses.** `SyntheticSite` excludes process startup
  and includes the RSS feed and sitemap. A published executable's wall clock, or a
  different site definition, is not comparable to it.
- **Do not edit `src/Kiji.SyntheticSite`'s site definition to improve a number.** It is a
  frozen, representative workload, kept separate from `src/Kiji.Tests/TestSite/` precisely
  so measurements stay comparable across commits. `Kiji.Benchmarks` references it rather
  than keeping a second copy.
- **Set an adoption bar before measuring, not after.** Define the required improvement
  before running the comparison.
- **Assert the variants agree.** In `[GlobalSetup]`, check that both strategies produce
  identical output, or the comparison is meaningless.
- **Server GC changes the answer.** It is off by default here; sites can opt in via
  `ServerGarbageCollection`. Say which one a number came from.

## CI

`.github/workflows/benchmarks.yml` runs the selected benchmarks (all, including whole builds, by default) — manual trigger plus a
weekly schedule, deliberately outside the PR-blocking pipeline because a full run is slow.
There is no end-to-end performance gate; regressions are caught by measuring deliberately,
not automatically.
