---
title: Performance
description: What makes builds fast, how incremental builds decide, and how to measure.
order: 50
---

## Full builds

Pages render in parallel — `Parallel.ForEachAsync` at the processor count, with a DI
scope and an `HtmlRenderer` per page. Markdown is read once per file, and that single
read yields the front matter, the body, and the content hash used by incremental builds.
Front matter is parsed by a hand-written span-based parser rather than a regex, and
Markdig's renderer is pooled per processor.

Writing is deliberately blunt: each page renders straight into a pooled UTF-8 buffer
(transcoded from UTF-16 as it goes, no intermediate string), then goes to disk as one
preallocated write. The output hash is computed from that same buffer, so a page is never
read back to fingerprint it.

Having to re-render a page does not mean rewriting it. Kiji compares the hash of what it
just rendered against what the last build recorded, and leaves the file alone while its
size and modification time still match — so editing a layout re-renders the whole site
but rewrites only the pages whose markup actually changed.

The output directory is never wiped. Once the build is done, Kiji walks it and deletes
whatever this build did not produce, which is both a stronger guarantee than deleting and
rewriting — it checks the directory rather than the last manifest's account of it — and
much cheaper.

For large sites built in-process, turning on server GC in your project file usually helps:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
</PropertyGroup>
```

## Incremental builds

`build` is incremental by default. The question it asks for each page is whether it can
*prove* the previous output is still valid; if it cannot, it re-renders.

During a render, Kiji records what the page actually read:

| What the page did | What it depends on |
| --- | --- |
| Read a markdown file's front matter, or rendered it | that file |
| Referenced a local image | that image, plus the variants it produced |
| Enumerated a content dictionary | the whole content set |
| Looked up an item by key | that item's file, if known; otherwise the content set |

So a post page depends on its own markdown file, while an index page that lists
everything depends on the content set. Editing one post re-renders that post, the pages
that list it, and the artifacts — not the site.

A page is preserved only when all of these hold: the global fingerprint matches (site
options, and the module version IDs of your assemblies), the route and parameters match,
the output file exists with the recorded hash, every additional output exists, and every
recorded dependency still matches.

Checks 3 and 5 are short-circuited by a stamp gate: the manifest also stores each file's
size and modification time, and while those match, the recorded hash is trusted and the
file is never opened. That turns a no-change rebuild from *read everything* into *stat
everything*.

Anything ambiguous falls back to re-rendering every page — a missing or corrupt manifest,
a schema change, or `-p:KijiForce=true`. A file in the output directory that no build produced is
not ambiguous: the reconciliation pass simply deletes it.

### Inputs Kiji cannot see

Renders are assumed deterministic in their inputs. If a page reads something Kiji cannot
observe — a data file loaded by a custom content source, an HTTP call — declare it:

```csharp
builder.AddBuildInput("data/authors.json");
builder.AddBuildInput("api-version", "2026-01");
```

Otherwise use `-p:KijiForce=true`. In-memory data derived from code is already covered, since
assembly module version IDs are part of the fingerprint. Framework assemblies are
excluded from that fingerprint, so after an SDK update use `-p:KijiForce=true` if you want
certainty.

## The dev server does less

`dev` pre-generates nothing. It renders the requested page on demand through the same
path the build uses, so its cost is one page render regardless of how large the site is.
Saving a markdown file re-reads that one file — an mtime and size token decides — then
the browser reloads over a WebSocket, debounced at 250 ms with duplicate events collapsed.

## Measuring

Kiji ships its own measurement rather than adjectives.
[`src/Kiji.Benchmarks`](https://github.com/zzzkan/kiji/tree/main/src/Kiji.Benchmarks) is
BenchmarkDotNet over the hot paths, including `SiteBuildBenchmarks`, which builds whole
200- and 1000-page sites across the four cases a site author actually waits on — a full
rebuild, no change at all, one post edited, and the site's own code changed:

```powershell
dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*SiteBuildBenchmarks*"
```

[`src/Kiji.SyntheticSite`](https://github.com/zzzkan/kiji/tree/main/src/Kiji.SyntheticSite)
generates an N-page site and measures full, no-change, and one-post-edited builds:

```powershell
dotnet run -c Release --project src/Kiji.SyntheticSite -- --pages 1000 --runs 3
```

It reports elapsed time, allocations, GC counts, and peak working set, and can write a
JSON report with `--out`. Any claim about Kiji's speed should come from one of these.
