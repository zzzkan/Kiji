---
name: measure-performance
description: Measure Kiji build performance and validate an optimization. Use when changing anything on the build hot path, when asked to make something faster, or before writing any performance number into prose.
---

# Measuring Kiji

The repository rule: **never write a performance number without a measurement in this
repository behind it.** Numbers typed into prose cannot be checked and rot silently.

## Which harness answers which question

| Question | Use | Why |
|---|---|---|
| Is this implementation faster than the one it replaces? | **BenchmarkDotNet** — put the old implementation beside the new one as `[Benchmark(Baseline = true)]` | Warmup, iteration counts, outlier removal, confidence intervals, and both variants run interleaved in one process under identical conditions |
| Did the whole build get faster? | **`SiteBuildBenchmarks`** (BenchmarkDotNet, `RunStrategy.Monitoring`) | Same statistics, applied to a real build. This is the authoritative end-to-end number |
| Which phase moved? | **`Kiji.SyntheticSite --phases`** | BenchmarkDotNet measures methods. Only the phase observer attributes time to snapshot / plan / clean / static / render / artifacts / entries / manifest |
| Is this commit faster than that commit? | **`Kiji.SyntheticSite`, A/B interleaved** | BenchmarkDotNet compares methods inside one assembly; it cannot compare two builds of `Kiji.dll` |

**Adopt or reject with BenchmarkDotNet. Confirm with the end-to-end job. Explain with
`--phases`.** A total that moved is not an explanation until a phase can be pinned on it.

```powershell
# End-to-end, all scenarios and sizes. The authoritative number.
dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*SiteBuildBenchmarks*"

# One hot path in isolation.
dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*Markdown*"

# Phase attribution, and cross-commit A/B.
dotnet run -c Release --project src/Kiji.SyntheticSite -- --pages 1000 --runs 5 --full --phases --root <dir>
```

`SiteBuildBenchmarks` covers `[Params]` of 200 / 1000 pages × `Full` / `NoChange` /
`OneEdited`. It reuses a corpus under the temp directory keyed by page count and leaves
it there on purpose — regenerating would put content generation inside the comparison.

`SyntheticSite` flags: `--pages N`, `--runs N`, `--images` (adds a cover image to every
tenth post), `--full` (delete `.kiji` before every run, so every run is a full build),
`--phases` (per-stage breakdown), `--root <dir>` (reuse a generated site), `--out <json>`.

## The two tools do not agree in absolute terms

Measured on the same corpus, minutes apart, 1000 pages, full build:

| | `SiteBuildBenchmarks` | `SyntheticSite` |
|---|---|---|
| total | ~900 ms | ~1180 ms |
| snapshot / clean / entries | 79 / 295 / 117 | 73 / 307 / 130 |
| **render** | **405** | **718** |

Every phase agrees except `render`, which the standalone harness reports ~75% higher —
reproducibly, and not because of its forced GC (tested) or its first-run JIT (the gap
survives fifteen runs). The cause is in the hosting process, not in Kiji.

So: **`SiteBuildBenchmarks` is the number you quote. `SyntheticSite` tells you which
phase moved and how much, relative to itself.** Never put a number from one beside a
number from the other.

## Using SyntheticSite by hand

It has no warmup, so its output needs reading with care:

- **Discard run 1.** The same binary is 500 ms slower on its first build than on its
  fifth — that is JIT, not the code. The median of `--runs 3` includes it.
- **Without `--full`, runs 2..N are no-change builds.** A median over them is not a
  full-build number. Pass `--full` when comparing full builds.
- **Check that the phase sum matches the run total.** The remainder is `KijiApp`
  construction and assembly scanning, which is ~0 once warm.

## Comparing two commits

The write phase is dominated by the filesystem and whatever scans it — it swings by
hundreds of milliseconds between sessions on the same binary. **Only an interleaved
comparison within a single session is valid.**

1. `git worktree add <dir> HEAD` for the baseline. Build both in Release.
2. Generate the corpus once and share it: both sides run with the same `--root`.
3. Alternate baseline / candidate for at least three rounds, `--runs 5 --full`.
4. Drop run 1 of every invocation; compare medians of what is left, per phase.
5. Report the delta with the commands and the environment (CPU, core count, .NET
   version, GC mode) beside it.

A single before-then-after pair is not evidence. A +37% "regression" measured that way
turned out to be +1.4% once interleaved — the difference was which session the numbers
came from.

## Traps

- **Do not compare numbers across harnesses.** `SyntheticSite` excludes process startup
  and includes the RSS feed and sitemap. A published executable's wall clock, or a
  different site definition, is not comparable to it.
- **Do not edit `src/Kiji.SyntheticSite`'s site definition to improve a number.** It is a
  frozen, representative workload, kept separate from `src/Kiji.Tests/TestSite/` precisely
  so measurements stay comparable across commits. `Kiji.Benchmarks` references it rather
  than keeping a second copy.
- **Set an adoption bar before measuring, not after.** `MarkdownRenderBenchmarks` states
  its own: adopt pooling only on a ≥5% win. Deciding afterwards is how noise becomes a
  feature.
- **Assert the variants agree.** In `[GlobalSetup]`, check that both strategies produce
  identical output, or the comparison is meaningless.
- **Server GC changes the answer.** It is off by default here; sites can opt in via
  `ServerGarbageCollection`. Say which one a number came from.

## CI

`.github/workflows/benchmarks.yml` runs the microbenchmarks only — manual trigger plus a
weekly schedule, deliberately outside the PR-blocking pipeline because a full run is slow.
There is no end-to-end performance gate; regressions are caught by measuring deliberately,
not automatically.
