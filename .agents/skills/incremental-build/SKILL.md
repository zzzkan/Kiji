---
name: incremental-build
description: Keep incremental builds correct when adding a content source, an artifact, a site option, or anything else a page can read. Use when a change introduces a new build input or output, or when a build skips a page it should have re-rendered.
---

# Incremental build invariants

A page is reusable only when the current inputs and the saved bytes prove it valid.
For content inputs and cached bytes, never substitute timestamps for content hashes, including within a long-lived app
across publishes. Code identity trusts compiler MVIDs; MSBuild owns recompilation.
Ambiguity falls back to rendering.

## Storage and publication

`Generation/IncrementalBuildPlanner.cs` drives the plan. Schema 6 stores dependencies and SHA-256 output hashes in `manifest.json`.
Reusable UTF-8 HTML occupies one immutable `html-*.bin` bundle referenced by the
manifest. Pages share slices of that bundle in memory. Write the complete bundle
before atomically publishing the manifest; retain the old bundle until publication succeeds.
Images remain separate SHA-256 blobs, with recipes in `image-records` for repair.
Only `.kiji/cache` is needed to reuse outputs in a clean checkout.

A cache lease serializes builds sharing a cache; page work stays parallel. Failed
builds retain the last successful manifest. Collect unused image blobs/recipes and
retired cache files only after publication succeeds. Output is reconciled against
all current pages, images, static files, and artifacts, including unknown files.

Unchanged builds retain both files. Cache-only restores update output stamps in metadata but retain the HTML bundle. Code changes
invalidate every page without loading the old HTML bundle. Uncacheable pages store
only metadata. Development writes under `.kiji/dev-site`, never `.kiji/cache`.
Restore only `.kiji/cache` in CI; development output is disposable.

## Dependencies

- Reading Markdown front matter or rendering its body records the source file and
  the hash of the exact bytes parsed in the current snapshot.
- A keyed content lookup records that entry's digest. Enumeration, Count, Keys,
  Values, or a missing key records the collection digest. Compute that digest once
  per materialization, including null when any item lacks a digest.
- Custom content uses stable entry IDs and declared digests. A missing digest
  disables reuse for readers; never assume arbitrary loader output is unchanged.
- Markdown projections retain source hashes. They must be independent per item.
- Local images record source bytes, processor identity, the repair request, and
  every materialized variant. Public URLs retain the page-bundle layout.
- External values/files are declared through `AddBuildInput`, `AddPageInput`, or
  `PageBuildInputs`. Use `PageBuildInputs.DisableCache()` for nondeterministic reads.

`ContentRuntime.Invalidate` and `ContentFileRegistry.Invalidate` advance the snapshot
before publishing or a dev reload. Reuse the hashes of freshly parsed bytes within
that snapshot; never reuse a prior snapshot merely because size/mtime match.
`VerifyInputs` deduplicates files consumed during rendering, rejects conflicting
reads, and verifies their hashes alongside entry construction/output reconciliation.
Always join verification before publishing the manifest, including on failure.

The retired `content-set` directory scanner and stamp gate no longer participate.
A collection's declared digests describe its actual contents, including filters and
custom content, rather than all Markdown files on disk.

## Reuse checks

1. Global options/declared inputs and code identities match. Assemblies use compiler
   MVIDs, including framework code. Missing, dynamic, or unavailable code cannot
   establish equivalence. No compiler-input records or binary hashes participate.
2. Route and supported deterministic parameter hashes match.
3. All recorded dependencies match the current snapshot.
4. Cached HTML bytes match their SHA-256. Missing/corrupt bytes rerender that page.
5. Kiji exclusively owns generated HTML and images. In the same output directory, matching size/mtime permits reuse; otherwise compare with verified cached bytes. Same-size/same-mtime external output edits are outside this contract. Image blobs are content-hash verified on restore,
   or outputs are restored from
   verified cached bytes. Missing/corrupt images can be repaired without rendering HTML.

Unsupported parameter values disable reuse. Missing, corrupt, or old-schema manifests
fall back to full rendering. `KijiForce` ignores the manifest. Re-rendered pages whose
HTML matches verified output skip rewriting that output.

## Adding inputs or outputs

- Add site-wide options to the options fingerprint. Declare unobservable data and
  configuration explicitly; compiler MVIDs identify changes to compiled code.
- A new output must be recorded or reconciliation will delete it. Keep image recipes
  sufficient to repair variants when only the cache is restored.
- Static files compare source/output hashes. RSS, sitemap, and custom artifacts are
  regenerated unconditionally; do not make them incremental.
- Preserve source-relative identities. Checkout paths and Git revision alone must
  not invalidate equivalent code/content in the supported Release site configuration.
  Changed dependency MVIDs conservatively invalidate pages.

## Verification

`IncrementalBuildTests` compares incremental and clean output byte for byte, checks
actual render skips, equal-size/equal-mtime mutations, orphan collection, failed-build
recovery, and concurrent publication. Extend relevant cases when changing the plan.
For changes to cache identity, MSBuild targets, or package layout, run
`./.agents/skills/incremental-build/scripts/Verify-PortableCache.ps1` from the repository.
It packs Kiji with compatibility validation and tests isolated Razor consumers:
different checkout/revision, clean binaries, content/code edits, cache corruption,
and compiler-observable PathMap changes. Fixtures and logs stay in ignored `artifacts`.

Release site executables use deterministic compilation with DebugType=none, omit
informational-version revisions, and normalize the project path through PathMap.
Keep these settings in Kiji.targets; do not reintroduce Kiji.Inputs.targets, sidecars,
compiler-input hashing, or timestamp-preserving source edit detection. Debug builds,
libraries, and tests retain their settings. Referenced projects are not automatically
configured; changed MVIDs cause full rendering. Preserve observable PathMap semantics
because CallerFilePath can reach rendered output. Post-compilation DLL edits retaining
the MVID are outside code change detection. Content and cached bytes still use hashes.

Use the measure-performance skill for hot-path changes. Keep the SyntheticSite
workload frozen. Keep input and cache-byte verification strict. The generated output stamp policy above is an explicit output ownership contract, not an input/cache shortcut.
