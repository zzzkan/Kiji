---
title: Performance
description: Incremental builds, external inputs, and a reproducible measurement procedure.
order: 50
---

Kiji prioritizes correct output over an optimistic reuse decision. It incrementally reuses
valid pages, but falls back to rebuilding whenever it cannot prove that an output is still
current. This page documents that behavior and how to establish performance baselines for
your workload; it does not make unqualified speed claims.

## Incremental builds

Publishing is incremental by default. Kiji reuses a page when its inputs and output
remain valid. Uncertain or missing build state causes a rebuild.

| What the page reads                        | What causes it to rebuild                           |
| ------------------------------------------ | --------------------------------------------------- |
| A Markdown item through its dictionary key | Changes to that item's source file                  |
| A local image                              | Changes to that image or missing generated variants |
| A content dictionary's entries             | Changes in that collection                          |
| A derived content dictionary               | Changes anywhere in the content tree                |

Use the dictionary indexer for individual posts. Searching through all entries makes
the page depend on the collection. Code and site configuration changes can rebuild all
pages. Feeds and sitemaps regenerate on every publish.

Files with unchanged size and modification time are assumed unchanged. If another tool
preserves both while replacing content, force a rebuild. Kiji removes files it did not
produce from the output directory: keep hand-managed files outside that directory and
put static assets in `wwwroot/`.

### External inputs

Declare files or values read by custom loaders or rendering code:

```csharp
app.AddBuildInput("data/authors.json");
app.AddBuildInput("api-version", "2026-01");
```

Update the declared value when remote data changes, or force a rebuild:

```pwsh
dotnet publish -p:KijiForce=true
```

Use a forced rebuild after an SDK update, since framework assemblies are excluded from
change detection. `-p:KijiVerbose=true` prints per-file build output.

## Development

`dotnet watch` renders requested pages on demand and reloads the browser after content
changes. A page that enumerates the whole collection still needs to load that collection.

## Measuring

Measure before describing a build as fast or comparing it with another generator. The
repository includes two complementary harnesses: a synthetic end-to-end site for publish
behavior and microbenchmarks for isolated operations. They answer different questions and
their numbers should not be combined.

Run the end-to-end harness from the repository root:

```pwsh
dotnet run -c Release --project src/Kiji.SyntheticSite -- --pages 1000 --runs 3
```

For individual operations and repeated site builds:

```pwsh
dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*"
```

See the repository's [measurement procedure](https://github.com/zzzkan/kiji/blob/main/.agents/skills/measure-performance/SKILL.md)
for comparable runs and recording results. Record the .NET SDK, operating system, CPU,
page and image counts, command line, run count, and median result. For the synthetic site,
report the full build, the unchanged rebuild, and the rebuild after editing one post.
