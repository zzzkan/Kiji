---
name: measure-performance
description: Measure Kiji build performance and validate an optimization. Use when changing anything on the build hot path, when asked to make something faster, or before writing any performance number into prose.
---

# Measuring Kiji

The repository rule: **never write a performance number without a measurement in this
repository behind it.** Numbers typed into prose cannot be checked and rot silently.

## Two harnesses, measuring different things

| | `src/Kiji.Benchmarks` | `src/Kiji.SyntheticSite` |
|---|---|---|
| Tool | BenchmarkDotNet | Hand-rolled harness |
| Scope | One hot path in isolation | A whole site build |
| Measures | Time and allocations per operation | Wall clock, allocations, GC counts, peak working set |
| Excludes | — | Process startup: it builds in-process with a fresh `KijiApp` per run |
| Use for | "Is this micro-optimization worth it?" | "Did the build actually get faster?" |

```powershell
# End-to-end. --pages/--runs as needed; --out writes JSON.
dotnet run -c Release --project src/Kiji.SyntheticSite -- --pages 1000 --runs 3

# Microbenchmarks. Standard BenchmarkDotNet args apply.
dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*Markdown*"
```

`SyntheticSite` flags: `--pages N`, `--runs N`, `--images` (adds a cover image to every
tenth post), `--full` (delete `.kiji` before every run, so every run is a full build),
`--root <dir>` (reuse a generated site instead of a fresh temp one), `--out <json>`.

Each invocation reports three numbers: run 1 is a full build, runs 2..N are no-change
builds, and a final run measures a rebuild after appending to one post. That last one is
the headline incremental metric.

## Before/after protocol

1. Generate the site once and keep it: `--root <dir>`. Reusing the same corpus removes
   content generation from the comparison.
2. Measure the baseline on the current commit. `--runs 3` minimum; take the median.
3. Apply the change, measure again on the same machine in the same session. Numbers from
   different machines, or from a machine doing other work, are not comparable.
4. Report the delta with the command and the environment (CPU, core count, .NET version)
   beside it.

For microbenchmarks, write the baseline as its own `[Benchmark]` method next to the new
one so BenchmarkDotNet compares them in a single run under identical conditions — this is
what `MarkdownRenderBenchmarks` and `PageWriteBenchmarks` already do. Assert in
`[GlobalSetup]` that both strategies produce identical output, or the comparison is
meaningless.

## Traps

- **Do not compare numbers across harnesses.** `SyntheticSite` excludes process startup
  and includes the RSS feed and sitemap. A number measured any other way — a published
  executable's wall clock, a different site definition — is not comparable to it, even
  though both are "seconds to build N pages".
- **Do not edit `src/Kiji.SyntheticSite`'s site definition to improve a number.** It is a
  frozen, representative workload, kept separate from `src/Kiji.Tests/TestSite/` precisely
  so measurements stay comparable across commits. Changing it invalidates every prior
  measurement.
- **Set an adoption bar before measuring, not after.** `MarkdownRenderBenchmarks` states
  its own: adopt pooling only on a ≥5% win. Deciding afterwards is how noise becomes a
  feature.
- **Server GC changes the answer.** It is off by default here; sites can opt in via
  `ServerGarbageCollection`. Say which one a number came from.

## CI

`.github/workflows/benchmarks.yml` runs the microbenchmarks only — manual trigger plus a
weekly schedule, deliberately outside the PR-blocking pipeline because a full run is slow.
There is no end-to-end performance gate; regressions are caught by measuring deliberately,
not automatically.
