---
name: incremental-build
description: Keep incremental builds correct when adding a content source, an artifact, a site option, or anything else a page can read. Use when a change introduces a new build input or output, or when a build skips a page it should have re-rendered.
---

# Incremental build invariants

`build` is incremental by default. The question it asks per page is not "did anything
change" but **"can I prove the previous output is still valid?"** — if it cannot prove it,
it re-renders. Every change here lives under one constraint: **never emit stale output.**

Code: `src/Kiji/Generation/IncrementalBuildPlanner.cs`, `BuildDependencyRecorder.cs`,
`BuildManifest*.cs`, `BuildFingerprint.cs` (XxHash128 throughout).

## What gets recorded

During a render, `PageRenderContext.Dependencies` collects what the page actually read:

| The page did this | It depends on |
|---|---|
| Read a `MarkdownContent`'s front matter, or called `RenderAsync` | `file:` that markdown file |
| Referenced a local image from markdown | `file:` the image, plus the variants as additional outputs |
| Enumerated a `ContentCollection` | `content-set` — a digest of every `*.md` under contents |
| `TryGet` / `GetRequired` by key | `file:` if the item carries provenance, otherwise `content-set` |

That last row is why provenance matters: `ContentCollection.Map` propagates it positionally
and sorting carries it along, so a keyed lookup collapses to a single-file dependency
instead of the whole content set. A post page depends on its own file; an index page that
enumerates depends on everything.

## When a page is preserved

All five must hold, or it re-renders:

1. Global fingerprint matches — the options hash (`SiteInfo`, relative path layout,
   `AddBuildInput` values) and the module version IDs of non-framework assemblies
2. Route and parameter hash match
3. The output file exists and its content hash matches the manifest
4. Every additional output exists
5. Every recorded dependency's fingerprint matches

Checks 3 and 5 short-circuit through a **stamp gate**: the manifest also stores each
file's `(length, lastWriteTimeUtc)`, and while those match, the recorded hash is trusted
and the file is never opened. A no-change rebuild is therefore `O(stat)`, not
`O(read everything)`. The documented hole is a rewrite preserving both size and mtime;
`--force` recovers.

## Adding something new

**A content source.** If items derive from files, implement `IContentSourceFile` so
provenance flows and keyed lookups stay file-scoped. Without it, every lookup falls back to
`content-set` — still correct, but every page then depends on all content.

**Anything Kiji cannot observe** — a data file read by a custom loader, an HTTP call, a
clock. Renders are *assumed deterministic in their inputs*. Declare it:

```csharp
builder.AddBuildInput("data/authors.json");   // hashes the file or directory
builder.AddBuildInput("api-version", "2026-01"); // any change re-renders everything
```

In-memory data derived from code needs nothing: assembly MVIDs already cover it.

**An artifact.** Artifacts are regenerated unconditionally — they depend on all page
metadata and are cheap. Do not make them incremental.

**A new site option.** Add it to the options hash in `ComputeOptionsHash`, or changing it
silently keeps stale output.

**A new kind of output.** Register it as an additional output on the page, or the orphan
sweep deletes it and check 4 never notices.

## Fallbacks are the safety net, not a failure

A full rebuild happens on a missing, corrupt, or schema-mismatched manifest, an unknown
file in the output directory, or `--force`. When a new situation is ambiguous, **fall back
to a full rebuild rather than reasoning about whether it is probably fine.**

The output directory is never wholesale deleted: skipped pages' outputs are preserved,
files in the previous manifest but not the current output set are removed as orphans, and
empty directories are reclaimed.

## The test that matters

`src/Kiji.Tests/IncrementalBuildTests.cs` asserts that a full build and a
full-build-then-edit-then-incremental-build produce **byte-identical output**. Any change
to this area extends that test. Also covered there, and worth extending alongside: that a
skip really skipped (output mtime unchanged), orphan collection, and each fallback trigger.

If a build skips a page it should have re-rendered, the bug is almost always a dependency
that was never recorded — start at `BuildDependencyRecorder` and the read path that should
have called into it, not at the skip logic.
