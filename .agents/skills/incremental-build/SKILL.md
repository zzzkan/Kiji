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
| Observed a `ContentDictionary`'s shape — `Count`, `Keys`, `Values`, enumeration | `content-set` scoped to that dictionary's directory |
| `this[key]` / `TryGetValue` / `ContainsKey` | `file:` if the item carries provenance, otherwise `content-set` |

That last row is why provenance matters: markdown sources state it explicitly so it
survives the projection into a user model, so a keyed lookup collapses to a single-file
dependency instead of the whole content set. A post page depends on its own file; an
index page that enumerates depends on everything in its scope. This is also why a page
must look items up with `dictionary[key]` rather than `dictionary.Values.First(x => ...)` —
the search enumerates, so it takes the `content-set` dependency and every detail page
re-renders on any content edit.

**Derived data is conservative by construction.** A tag list or related-posts computation
reads the whole dictionary, so the page doing it records `content-set` and re-renders on any
content change — which is genuinely what it depends on. Computing it inside the page is
the recommended shape precisely because the dependency then falls out of the enumeration
with no rules to remember.

**Scopes.** A `content-set` dependency's manifest `Key` is the dictionary's
contents-relative directory (`MarkdownContentOptions.Directory`), empty for the whole
tree, and the planner fingerprints each scope separately. Two dictionaries over different
directories therefore do not invalidate each other's index pages. Builds predating this
wrote `contents` as the key; that is still read as the whole tree.

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
`-p:KijiForce=true` recovers.

## Adding something new

**A content source.** The key comes from the selector passed at registration. If items
derive from files, implement `IContentSourceFile` (or have the loader supply provenance, as
the markdown source does for projected models) so keyed lookups stay file-scoped. Without
it, every lookup falls back to `content-set` — still correct, but every page then depends
on all content.

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

**A new kind of output.** Register it as an additional output on the page, or the reconciliation
sweep deletes it and check 4 never notices.

## Fallbacks are the safety net, not a failure

Every page re-renders on a missing, corrupt, or schema-mismatched manifest, on a changed
options hash or assembly MVID, or on `-p:KijiForce=true`. When a new situation is ambiguous, **fall
back to re-rendering rather than reasoning about whether it is probably fine.**

**The output directory is never wholesale deleted.** After the build, `ReconcileOutputs`
walks it and deletes every file the new manifest does not claim, then reclaims empty
directories. That is why an unknown file in the output directory is *not* a reason to
re-render anything — it is simply removed. Reconciliation checks the directory itself
rather than the previous manifest's account of it, so it is both stronger and cheaper
than deleting and rewriting (~64 ms against ~527 ms on a 1000-page tree,
`Kiji.Benchmarks OutputCleanBenchmarks`).

**Re-rendering a page does not mean rewriting it.** `WritePageAsync` compares the hash of
what it just rendered against the previous manifest entry, and skips the write while the
output file's stamp still matches what was recorded with that hash. Editing a layout
re-renders every page but rewrites only the ones whose markup actually changed. `-p:KijiForce=true`
loads no manifest, so it always writes — that is what it is for.

## The test that matters

`src/Kiji.Tests/IncrementalBuildTests.cs` asserts that a full build and a
full-build-then-edit-then-incremental-build produce **byte-identical output**. Any change
to this area extends that test. Also covered there, and worth extending alongside: that a
skip really skipped (output mtime unchanged), orphan collection, and each fallback trigger.

If a build skips a page it should have re-rendered, the bug is almost always a dependency
that was never recorded — start at `BuildDependencyRecorder` and the read path that should
have called into it, not at the skip logic.
