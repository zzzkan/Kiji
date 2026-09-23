# Kiji

A .NET static site generator: Razor components rendered with Blazor's HtmlRenderer.

## Commands and layout

- `dotnet build -c Release` and `dotnet test -c Release` cover the whole solution.
  Pass no extra flags to test: unsupported flags reach Microsoft.Testing.Platform.
- `dotnet publish docs -c Release -o docs/dist` generates docs;
  `dotnet watch --project docs` serves it. Docs references the published package;
  use an isolated consumer of the local package to verify framework changes.
- `src/Kiji` is the single package, including Markdown, images, feeds and hosting.
  `MSBuild/Kiji.targets` integrates publish, watch and clean.
- `src/Kiji.Tests` contains xUnit v3 tests and TestSite integration fixtures.
  Classes run in parallel; never mutate process-wide state.
- `src/Kiji.SyntheticSite` is the frozen end-to-end workload; `src/Kiji.Benchmarks`
  contains BenchmarkDotNet measurements. Do not change the workload to improve results.
- Stop any verification server/watch process before completion. Stop only processes
  started by the current task.

## Design

- Build with Create -> Add*/Use* -> RunAsync. Kiji parses no command line:
  watch serves, publish generates, clean cleans. MSBuild owns KijiForce/KijiVerbose;
  ASP.NET Core configuration owns the dev server address. Prefer conventions to flags.
- Dev and publish share StaticSite.RenderPageAsync. Never emit stale output;
  ambiguous incremental state requires rendering.
- One type per file. Components in src are hand-written ComponentBase classes;
  only docs uses .razor files and the Razor SDK.
- Update `docs/contents/api-reference/index.md` for author-facing API changes.
  Keep pack's baseline validation enabled; intentional breaks get only their specific
  entries in `src/Kiji/CompatibilitySuppressions.xml`.
- Tests must catch realistic regressions, not repeat library guarantees or visibility
  checks already covered by compilation/pack. Keep experiments/reports in ignored artifacts.

## Constraints worth preserving

- Routes allow literals and simple parameters only. UseNotFoundPage still requires
  its component's own route. AddPages supplies the whole parameter set; non-route
  values must name writable [Parameter] properties.
- SitePaths chooses the nearest project ancestor, then Git ancestor, then cwd.
  Content becomes accessible through factories/loaders/artifacts only after execution
  paths settle. Do not expose a provider or content dictionary on StaticSite.
- The dev server enforces BaseUrl's path prefix and uses `.kiji/dev-site`.
  Only `.kiji/cache` is portable; planning creates no output directories.
- AddPageService instances belong to one render and are disposed with its scope.
  Loaders, route/feed factories and artifacts cannot resolve them. Derived indexes
  use UseContentSource. There is no public service collection.
- UseImageProcessor creates one lazy, site-owned processor, disposed with the site.
  It must support concurrency and retain no page/content state. Reload does not recreate
  it. Declare external configuration with AddBuildInput and version cache identities.

Do not reintroduce evaluated alternatives: pooled HtmlRenderer (root/scope state cannot
reset), Blazor SectionOutlet/RouteView (renderer-owned state), AOT/trimming (reflection),
source-generated page discovery (cached scanning suffices), IBufferWriter rendering
(HtmlRootComponent exposes TextWriter), content-addressed image URLs (break page bundles),
or incremental RSS/sitemap (depend on all metadata and are cheap to regenerate).

## Skills

Read before working in the corresponding area; keep reusable verification here:

- `.agents/skills/measure-performance/SKILL.md`: required for hot-path changes and
  performance claims; harness selection, saved baselines and interleaved comparisons.
- `.agents/skills/incremental-build/SKILL.md`: required for new inputs/outputs or
  reuse changes; dependency tracking, cache publication and portable-cache verification.
