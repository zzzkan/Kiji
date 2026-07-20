# Benchmarks

Kiji's performance work is grounded in two kinds of measurement, both living in
this repository:

1. **Kiji.Benchmarks** (`src/Kiji.Benchmarks`): BenchmarkDotNet
   microbenchmarks for the hot paths (front matter parsing, page writing,
   component rendering, markdown rendering). Run locally with:

   ```powershell
   dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*"
   ```

   CI runs these in a dedicated workflow
   ([benchmarks.yml](../.github/workflows/benchmarks.yml)) — manually
   triggerable (with an optional filter) plus a weekly schedule, kept out of
   the PR-blocking pipeline because a full BenchmarkDotNet run takes
   significant time. Each run publishes the result tables to the workflow
   summary and uploads the raw reports as an artifact.

2. **Kiji.SyntheticSite** (`src/Kiji.SyntheticSite`): an end-to-end harness
   that generates an N-page deterministic site and measures full / no-change /
   one-post-edited builds — wall clock, allocations, GC counts, and peak
   working set. This is a local tool for before/after comparisons while
   optimizing (`--out` writes a JSON report):

   ```powershell
   dotnet run -c Release --project src/Kiji.SyntheticSite -- --pages 1000 --runs 3
   ```

Any claim about Kiji's speed should cite one of these measurements, not a
number typed into prose.
