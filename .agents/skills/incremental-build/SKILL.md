---
name: incremental-build
description: Keep incremental builds correct when adding a content source, an artifact, a site option, or anything else a page can read. Use when a change introduces a new build input or output, or when a build skips a page it should have re-rendered.
---

## Reuse invariants

Ambiguity always falls back to rendering. A page may be reused only when:

1. Global options/declared inputs and code identities match. Code identity uses
   compiler MVIDs, including framework assemblies; unavailable code invalidates.
2. Route and deterministic parameter fingerprints match. Unsupported values disable reuse.
3. Recorded dependencies match the current snapshot.
4. Cached HTML bytes pass SHA-256 verification. Missing/corrupt bytes rerender that page.
5. Outputs exist or can be restored/repaired from verified data.

Content inputs and cached bytes always use hashes, never timestamps. Kiji exclusively
owns generated HTML/images: in the same output directory a matching size/mtime can
reuse an output; otherwise compare with verified bytes. Same-size/same-mtime external
output edits are outside this ownership contract. This exception never applies to inputs.

## Dependencies

- Markdown front matter/body reads record the hash of the bytes actually parsed.
  Independent per-item projections retain that hash.
- Keyed content lookup records the entry digest. Enumeration, Count, Keys, Values
  and missing keys record the collection digest, computed once per materialization.
  Stable IDs identify custom entries; a missing digest disables reuse for readers.
- Images record source bytes, processor identity, repair requests and variants.
  Keep public image URLs in the page bundle and recipes sufficient for cache-only repair.
- Declare external data through AddBuildInput, AddPageInput or PageBuildInputs.
  DisableCache handles nondeterministic reads.
- Invalidate content, file hashes and value resolutions before each publish/dev reload.
  Verify inputs before manifest publication, including joining verification on failure.
  Conflicting reads within one build must fail.

## Publication

IncrementalBuildPlanner owns planning and manifest publication. Only `.kiji/cache`
travels between checkouts; development uses `.kiji/dev-site`.

- Serialize builds sharing a cache with a lease; page work remains parallel.
- Publish the complete immutable HTML bundle before atomically replacing its manifest.
  Retain the last successful manifest/bundle on failure; collect retired files afterward.
- No-change builds retain manifest and bundle. Cache-only restores may update output
  stamps without replacing HTML. Code changes need not load old HTML. Uncacheable
  pages retain metadata only.
- Reconcile all pages, images, static files and artifacts, removing unknown outputs.
  Static files compare hashes; feeds, sitemaps and custom artifacts always regenerate.
- Missing, corrupt or incompatible manifests trigger full rendering. KijiForce ignores
  the manifest; identical freshly rendered output need not be rewritten.

## Changes and verification

Add site-wide settings to the options fingerprint and record every new output.
Keep identities source-relative so equivalent checkouts reuse cache.

IncrementalBuildTests checks clean/incremental byte equality, actual render skips,
equal-size/equal-mtime content edits, orphan cleanup, failure recovery and concurrent
publication. Extend relevant regression cases when changing the planner.

For cache identity, MSBuild or package changes, run:

```pwsh
./.agents/skills/incremental-build/scripts/Verify-PortableCache.ps1
```

It validates the package and isolated Razor consumers across checkout/revision,
content/code edits, corruption, PathMap and build modes. Logs stay in `artifacts`.
Release site executables normalize source/revision metadata and omit debug symbols;
Debug, libraries and tests retain their settings. MSBuild owns source recompilation.
Preserve observable CallerFilePath semantics; changed dependency MVIDs invalidate.
Post-compilation binary edits retaining the MVID are outside code change detection.

Use the measure-performance skill for hot-path changes.
