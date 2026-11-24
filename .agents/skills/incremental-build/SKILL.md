---
name: incremental-build
description: Keep incremental builds correct when adding a content source, an artifact, a site option, or anything else a page can read. Use when a change introduces a new build input or output, or when a build skips a page it should have re-rendered.
---

## Reuse invariants

Ambiguity always falls back to rendering. A page may be reused only when:

1. Global options and code identities match. Code identity uses compiler MVIDs, including
   framework assemblies; unavailable code invalidates.
2. Route and deterministic parameter fingerprints match. Unsupported values disable reuse.
3. Recorded dependencies match the current snapshot.
4. Cached HTML bytes pass SHA-256 verification. Missing/corrupt bytes rerender that page.
5. Outputs exist or can be restored/repaired from verified data.

Content inputs and cached bytes always use hashes, never timestamps. Kiji exclusively owns
generated HTML/images: in the same output directory a matching size/mtime can reuse an
output; otherwise compare with verified bytes. Same-size/same-mtime external output edits are
outside this contract, which never applies to content inputs. Static web assets follow the
SDK's metadata caching: edits must update modification times. Keep normal SDK caching and
ProjectReference properties so shared projects are not built with conflicting global
properties.

## Dependencies

- Markdown body reads, ID lookups and item parameters record the whole-file hash.
  FrontMatter reads record the YAML text digest against the owning collection source.
  Parsed-source reuse still checks the whole file.
- Enumeration, Count and missing IDs record the collection digest, computed once per
  materialization from ordered IDs and projection digests. A projection digest covers front
  matter only, unless the projection called RenderAsync.
- Collection item parameters hash source/ID identity and record item digests; reject stale
  snapshot items. Parsed front matter can return in a later snapshot. Strings hash by value;
  an instance shared by several items is unidentified.
- Images record source bytes, processor identity, repair requests and variants. Every
  referring page records the shared `_images/` outputs and recipes sufficient for cache-only
  repair. Reused pages may keep older custom processor results while newly rendered pages
  reference another immutable version. External files follow Markdown's nonportable-page
  policy: they may render, but cannot produce portable page recipes. Check source containment
  and descendant links on reuse and repair as well as rendering. LinkTarget, rather than
  ReparsePoint alone, distinguishes redirects from cloud placeholders and deduplicated files.
- Any output page rendering an AlwaysRender component is uncacheable, including
  nested/dynamic children. AlwaysRender adds no watchers and clears no image caches. Do not
  reintroduce per-input registration APIs.
- Invalidate content, file hashes and value resolutions before each publish/dev reload.
  Verify inputs before manifest publication, including joining verification on failure.
  Conflicting reads within one build must fail.
- Add site-wide settings to the options fingerprint, record every new output, and keep
  identities source-relative so equivalent checkouts reuse the cache.

## Publication

IncrementalBuildPlanner owns planning and manifest publication.

- Only `.kiji/cache` (pages) and `.kiji/jit` (publish JIT profile) travel between checkouts;
  the dev server uses `.kiji/dev-site`. Image work stays in private `.kiji/cache/work` (under
  the publish lease) or `.kiji/dev-work`; remove abandoned work before the next execution.
- Serialize builds sharing a cache with a lease; page work remains parallel.
- Publish the complete immutable HTML bundle before atomically replacing its manifest. Keep
  the last successful manifest/bundle on failure; collect retired files afterward.
- No-change builds keep manifest and bundle. Cache-only restores may update output stamps
  without replacing HTML. Code changes need not load old HTML. Uncacheable pages keep
  metadata only.
- Reconcile all pages, images, static files and artifacts, removing unknown outputs. Static
  files compare hashes; feeds, sitemaps and custom artifacts always regenerate.
- Planning creates no output directories; a page creates its own when it writes. When the
  output directory does not exist as a build begins, every file in it comes from that build:
  pages write without comparing, read their own stamps, and reconciliation reads the rest one
  file at a time instead of listing the tree. Stamps from an earlier manifest are cleared.
- Missing, corrupt or incompatible manifests trigger full rendering.
- Dev reloads keep built-in immutable variants, retire custom outputs best-effort after
  joining render work, and never wait for response transmission. Ordinary saves and watcher
  errors do not recreate native watchers; only retargeted content links replace theirs.
- `.kiji/jit` never affects output or reuse, so CI can restore it with or without
  `.kiji/cache`; it is saved atomically under the cache lease after a successful publish. The
  runtime writes a profile one record at a time, so a publish keeps the one it replayed when
  the stamp shows the same code recorded it, to the end of a publish, over at least as many
  pages. A publish with no profile for its code ends the recording with rendering; the next
  publish records through to the end once. The recording has its own directory because
  removing it is the only way to make the runtime drop it.

## Code identity

Release site executables normalize source/revision metadata and omit debug symbols; Debug,
libraries and tests keep their settings. MSBuild owns source recompilation. Preserve
observable CallerFilePath semantics; changed dependency MVIDs invalidate. Post-compilation
binary edits that keep the MVID are outside code change detection.

## Tests

Extend `IncrementalBuildTests` for planner changes, `StaticAssetTests` for byte/URL/path and
stale-source asset cases, and `Kiji.PackageTests` for package journeys; do not add another
static-asset harness or assert storage optimizations as Kiji behavior. Run
`tests/PortableCache/Verify-PortableCache.ps1` (also a CI job) for cache identity, MSBuild or
package changes.
