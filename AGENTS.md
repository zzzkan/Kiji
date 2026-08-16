# Kiji

A static site generator framework for .NET. Pages are Razor components rendered to
static HTML via Blazor's `HtmlRenderer`, assembled with a minimal-API style builder.

## Commands

- `dotnet build -c Release` and `dotnet test -c Release` — both operate on the whole solution
- Pass no extra flags to `dotnet test`. Unrecognized ones (`--nologo`) reach the
  Microsoft.Testing.Platform runner, which prints help and exits 5
- `dotnet run --project docs -- build` — build the docs site; `-- dev` and `-- preview` also work
- `dotnet run -c Release --project src/Kiji.SyntheticSite -- --pages 1000 --runs 3` — end-to-end build perf
- `dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*"` — microbenchmarks

## Design constraints

- **One package.** Markdown, images, feeds, sitemaps, and the dev server all ship in
  `Kiji`. Do not split into extension packages.
- **Flagless API.** Build with `CreateBuilder` → `Map*` → `RunAsync`. Prefer conventions
  and a few explicit APIs over option flags. CLI flags are limited to execution control
  (`--force`, `--verbose`, `--port`).
- **`dev` and `build` share the rendering path.** Both go through
  `KijiApp.RenderPageAsync`, so what `dev` shows is what `build` writes. Keep it that way.
- **Correctness outranks speed.** Every optimization, incremental builds included, lives
  under "never emit stale output". Anything ambiguous falls back to a full rebuild.
- **One type per file.**
- **Kiji never generates an `href`.** Linking is the site author's job.

## Already evaluated and rejected — do not reintroduce

| Idea | Why not |
|---|---|
| `<base href>` for base paths | Page-bundle images use `./` document-relative URLs written beside the page; `<base>` re-roots and breaks them |
| Rewriting hrefs after render | Kiji does not generate hrefs; post-hoc classification of fragments, queries, and external URLs is inaccurate and costs a full re-parse |
| A `--base-path` CLI flag | `SiteInfo.BaseUrl` is the single source of truth |
| Pooling Blazor's `HtmlRenderer` | Root component state cannot be reset via public API, per-page scoped services are captured at construction, and setup is only ~10% of a page render |
| Blazor's `SectionOutlet` / `RouteView` | Their state lives on the renderer, out of Kiji's control; replaced by `HeadOutlet` / `PageView` with state on the DI scope |
| Native AOT / trimming | `HtmlRenderer`, `ParameterView`, and YamlDotNet are reflection-based; SSG is throughput-bound so AOT's wins do not apply |
| Source-generated page discovery | One cached assembly scan is milliseconds |
| Rendering to `IBufferWriter<byte>` | `HtmlRootComponent` only exposes `WriteHtmlTo(TextWriter)` |
| Content-addressed image output | Breaks the page-bundle layout and `./` references |
| Incremental RSS/sitemap | They depend on all page metadata and are cheap to regenerate |

## Conventions

- Components in `src/` are hand-written `ComponentBase` subclasses with `BuildRenderTree`
  and `[Route]`. `.razor` files exist only in `docs/`, which is the sole Razor SDK consumer.
- Tests are xUnit v3 on Microsoft.Testing.Platform, with `InternalsVisibleTo` to
  `Kiji.Tests`. Test classes run in parallel, so never mutate process-wide state
  (`Directory.SetCurrentDirectory`, environment variables) — extract an internal overload
  taking the inputs instead.
- Integration tests build against `src/Kiji.Tests/TestSite/`. `src/Kiji.SyntheticSite`'s
  site definition is a frozen benchmark workload — keep it separate so measurement stays
  comparable.
- Never write a performance number in prose without a measurement in the repo behind it.

## Gotchas

- Route templates reject catch-all segments, route constraints, optional parameters, and
  composite segments. A static site must know every URL up front.
- `MapNotFound<T>()`'s component still needs its own `@page`/`[Route]`, or it throws.
- `SitePaths.Root` defaults to the nearest ancestor with a project file, then the nearest
  with `.git`, then the current directory.
- `SiteInfo.Path(...)` throws on `./` and `../`: document-relative URLs must stay untouched.
- `dev` and `preview` serve under `SiteInfo.BaseUrl`'s path and return 404 outside it, on
  purpose — a link that forgot `Site.Path` should fail locally, not after deploy.
- Framework (`System.*`/`Microsoft.*`) assemblies are excluded from the incremental build
  fingerprint. After an SDK update, use `--force` to be certain.

## Skills

Longer procedures are kept out of this file so it stays short. Read the relevant one
before working in that area:

- `.agents/skills/measure-performance/SKILL.md` — which harness measures what, the
  before/after protocol, and why numbers from different harnesses are not comparable.
  Read before changing anything on the build hot path or writing a performance number.
- `.agents/skills/incremental-build/SKILL.md` — what gets recorded as a dependency, the
  five skip conditions, and what to do when adding a content source, artifact, option, or
  output. Read before introducing a new build input or output.

## Repository layout

- `src/Kiji` — the framework
- `src/Kiji.Tests` — unit and integration tests
- `src/Kiji.Benchmarks` — BenchmarkDotNet microbenchmarks
- `src/Kiji.SyntheticSite` — end-to-end build performance harness
- `docs` — the documentation site, built with Kiji and deployed to GitHub Pages
- `templates` — the `dotnet new kiji` template package. It sits outside `src/` because the
  scaffolded site references `Kiji` from NuGet, not from this tree, so it has no build-time
  dependency on the framework. `eng/verify-template.ps1` packs, scaffolds, and builds it
