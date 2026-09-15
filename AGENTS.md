# Kiji

A static site generator framework for .NET. Pages are Razor components rendered to
static HTML via Blazor's `HtmlRenderer`, assembled with a minimal-API style app.

## Commands

- `dotnet build -c Release` and `dotnet test -c Release` — both operate on the whole solution
- Pass no extra flags to `dotnet test`. Unrecognized ones (`--nologo`) reach the
  Microsoft.Testing.Platform runner, which prints help and exits 5
- `dotnet publish docs -c Release -o docs/dist` — generate the docs site; `dotnet watch --project docs` serves it
- `dotnet run -c Release --project src/Kiji.SyntheticSite -- --pages 1000 --runs 3` — end-to-end build perf
- `dotnet run -c Release --project src/Kiji.Benchmarks -- --filter "*"` — microbenchmarks

## Design constraints

- **One package.** Markdown, images, feeds, sitemaps, and the dev server all ship in
  `Kiji`. Do not split into extension packages.
- **Flagless API.** Build with `Create` → `Add*` / `Use*` → `RunAsync`. Prefer conventions
  and a few explicit APIs over option flags. The app parses **no** command line of its own:
  `dotnet watch` serves, `dotnet publish` generates, `dotnet clean` cleans. Execution
  control lives on MSBuild (`-p:KijiForce`, `-p:KijiVerbose`) and the dev server's address
  comes from ASP.NET Core configuration.
- **The dev server and a publish share the rendering path.** Both go through
  `StaticSite.RenderPageAsync`, so what the dev server shows is what a publish writes.
  Keep it that way.
- **Correctness outranks speed.** Every optimization, incremental builds included, lives
  under "never emit stale output". Anything ambiguous falls back to a full rebuild.
- **One type per file.**

## Already evaluated and rejected — do not reintroduce

| Idea                                   | Why not                                                                                                                                             |
| -------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| Pooling Blazor's `HtmlRenderer`        | Root component state cannot be reset via public API, per-page scoped services are captured at construction |
| Blazor's `SectionOutlet` / `RouteView` | Their state lives on the renderer, out of Kiji's control; replaced by `HeadOutlet` / `PageView` with state on the DI scope                          |
| Native AOT / trimming                  | `HtmlRenderer`, `ParameterView`, and YamlDotNet are reflection-based; SSG is throughput-bound so AOT's wins do not apply                            |
| Source-generated page discovery        | The cached assembly scan does not justify generated discovery                                                                                                            |
| Rendering to `IBufferWriter<byte>`     | `HtmlRootComponent` only exposes `WriteHtmlTo(TextWriter)`                                                                                          |
| Content-addressed image output         | Breaks the page-bundle layout and `./` references                                                                                                   |
| Incremental RSS/sitemap                | They depend on all page metadata and are cheap to regenerate                                                                                        |

## Conventions

- Components in `src/` are hand-written `ComponentBase` subclasses with `BuildRenderTree`
  and `[Route]`. `.razor` files exist only in `docs/`, which is the sole Razor SDK consumer.
- Tests are xUnit v3 on Microsoft.Testing.Platform, with `InternalsVisibleTo` to
  `Kiji.Tests`. Test classes run in parallel, so never mutate process-wide state.
- Integration tests build against `src/Kiji.Tests/TestSite/`. `src/Kiji.SyntheticSite`'s
  site definition is a frozen benchmark workload — keep it separate so measurement stays
  comparable.

## Gotchas

- Route templates reject catch-all segments, route constraints, optional parameters, and
  composite segments. A static site must know every URL up front.
- `UseNotFoundPage<T>()`'s component still needs its own `@page`/`[Route]`, or it throws.
- `SitePaths.RootDirectory` defaults to the nearest ancestor with a project file, then the nearest
  with `.git`, then the current directory.
- `SiteInfo.Path(...)` throws on `./` and `../`: document-relative URLs must stay untouched.
- The dev server serves under `SiteInfo.BaseUrl`'s path and returns 404 outside it, on
  purpose — a link that forgot `Site.Path` should fail locally, not after deploy.
- Framework (`System.*`/`Microsoft.*`) assemblies are excluded from the incremental build
  fingerprint. After an SDK update, publish with `-p:KijiForce=true` to be certain.
- The object `AddPages` yields is the page's whole parameter set, not just route values.
  Names matching the route template bind the URL; the rest must be declared `[Parameter]`
  properties on the component, or planning fails naming them.
- The `IServiceProvider` handed to a route factory, a feed factory, a content loader, or
  a site artifact is the earliest point content can be reached. `StaticSite` exposes neither
  a dictionary nor a provider on purpose: loading content resolves `ResolvedSitePaths`, a singleton
  the run settles, so an earlier read would pin publish paths onto the dev server.
- Register page helpers with `AddPageService<T>()`: one instance per page render, shared
  by the page, layout, and children and disposed after rendering. Loaders, route/feed
  factories, and artifacts cannot resolve page services. Share derived indexes through
  `AddContentSource<T>`, not page services. There is no public service collection.
- Replace image processing with `UseImageAssetProcessor(() => new CustomProcessor())`.
  The site owns one lazy processor, including disposal; it must support concurrent calls
  and retain no page/content state. Reloading content does not recreate it. Declare
  external configuration with `AddBuildInput` and version custom image cache identities.

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

- `src/Kiji` — the framework. `src/Kiji/MSBuild/Kiji.targets` is what makes
  `dotnet publish` generate the site and replaces the publish output with it
- `src/Kiji.Tests` — unit and integration tests
- `src/Kiji.Benchmarks` — BenchmarkDotNet microbenchmarks
- `src/Kiji.SyntheticSite` — end-to-end build performance harness
- `docs` — the documentation site, built with Kiji and deployed to GitHub Pages
- `templates` — the `dotnet new kiji` template package.
