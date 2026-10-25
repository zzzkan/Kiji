# Kiji

A .NET static site generator: Razor components rendered with Blazor's HtmlRenderer.

## Commands and layout

- `dotnet build -c Release` and `dotnet test -c Release` cover the whole solution.
  Pass no extra flags to test: unsupported flags reach Microsoft.Testing.Platform.
- `dotnet publish docs -c Release -o docs/dist` generates docs;
  `dotnet watch --project docs` serves it. Docs references the local Kiji project
  and imports its publish targets. Use an isolated consumer of the packed local
  package to verify package integration.
- `src/Kiji` is the single package, including Markdown, images, feeds and hosting.
  `MSBuild/Kiji.targets` integrates publish, watch and clean.
- `src/Kiji.Tests` contains library unit/component tests and TestSite fixtures.
  Classes run in parallel; never mutate process-wide state. It does not pack or build consumers.
- `src/Kiji.PackageTests` contains serial, black-box package scenarios with one fixed
  consumer workspace. External dependencies use NuGet's global cache; only Kiji
  is isolated. See its README for execution and cleanup.
  Run `dotnet test -c Release` from either test project to select that level.
- `src/Kiji.SyntheticSite` is the frozen end-to-end workload; `src/Kiji.Benchmarks`
  contains BenchmarkDotNet measurements. Do not change the workload to improve results.
- Stop any verification server/watch process before completion. Stop only processes
  started by the current task.

## Design

- Follow the .NET host builder shape: CreateBuilder -> builder.Services/Add*/Use* -> Build
  -> RunAsync. Every registration lives on StaticSiteBuilder; StaticSite only runs. Add* adds,
  Use* sets a single value. Kiji parses no command line:
  watch serves, publish generates, clean cleans. MSBuild owns KijiForce/KijiVerbose;
  ASP.NET Core configuration owns the dev server address. Prefer conventions to flags.
- Dev and publish share StaticSite.RenderPageAsync. Never emit stale output;
  ambiguous incremental state requires rendering.
- One type per file. Library components and library test components are hand-written ComponentBase classes;
  docs and the package consumer fixture use .razor files and the Razor SDK.
- Update `docs/content/api-reference/index.md` for author-facing API changes.
  Keep pack's baseline validation enabled; intentional breaks get only their specific
  entries in `src/Kiji/CompatibilitySuppressions.xml`.
- Tests must catch realistic regressions, not repeat library guarantees or visibility
  checks already covered by compilation/pack. Keep experiments/reports in ignored artifacts.

## Constraints worth preserving

- Discover all routes in the entry assembly automatically; other assemblies require
  AddPageAssembly. Routes allow literals and simple parameters only. Parameterized routes
  require AddPageParameters; an empty sequence generates no pages. Each entry is a whole
  parameter set rendered at every parameterized route; fixed routes beside them keep default
  rendering, while components with only fixed routes receive the sets. Supplied values must
  name writable [Parameter] properties; do not enforce EditorRequired. UseNotFoundPage still
  requires its own route and a registered page assembly.
- MSBuild supplies the project directory through runtimeconfig; missing metadata is an error.
  Content becomes accessible through factories/loaders/artifacts only after execution
  paths settle. Do not expose a provider or content dictionary on StaticSite. The environment
  name comes from DOTNET_ENVIRONMENT, else Production for publish and Development for serving;
  IHostEnvironment is read-only because it selects the configuration sources at creation,
  and is part of the options fingerprint. Configuration defaults to project-local sources only
  (appsettings*.json, user secrets in Development): no environment variables or command line,
  which differ between machines and between serve and publish. Every configuration key and value, nulls included, is
  hashed into the options fingerprint because per-key reads are not tracked.
- The dev server enforces BaseUrl's path prefix and uses `.kiji/dev-site`.
  Only `.kiji/cache` is portable; planning creates no output directories.
- StaticSiteBuilder.Services is standard DI; Build validates it, makes it read-only and
  builds the provider. Kiji's defaults and managed services are registered when the builder
  is created. Each render owns a scope. Loaders, page parameter/feed
  factories and artifacts use the root provider and cannot resolve scoped services. Validate
  Kiji-owned registrations in Build. Derived indexes use AddContentSource;
  content reload does not recreate singletons. Use AlwaysRender on components reading
  untracked external state. KijiForce refreshes all pages for a whole publish.
- Use Blazor's PageTitle and HeadContent; KijiRoot supplies the single HeadOutlet.
  Each page gets a fresh renderer/dispatcher, isolating the framework's section state.
  HeadContent replaces earlier providers rather than appending; compose shared tags
  inside the page's HeadContent. Await quiescence before serializing the document.
- Customize IImageProcessor through builder.Services. The last unkeyed registration must
  be a singleton; standard DI ownership applies. It must support concurrency and retain
  no page/content state. Reload does not recreate it. Include external configuration
  and encoder versions in cache identities; HTML reuse controls do not clear image caches.
  The default processor reads ImageProcessorOptions (IOptions) once; ValidateOnStart validates them
  when the site starts running (IStartupValidator, IAsyncStartupValidator from .NET 11), never in Build,
  and every setting must stay in its CacheIdentity. Null Widths means DefaultWidths so
  configuration binding cannot append to the defaults.

Do not reintroduce evaluated alternatives: pooled HtmlRenderer (root/scope state cannot
reset), RouteView (PageView owns Kiji's layout resolution), AOT/trimming (reflection),
source-generated page discovery (cached scanning suffices), IBufferWriter rendering
(HtmlRootComponent exposes TextWriter), content-addressed image URLs (break page bundles),
or incremental RSS/sitemap (depend on all metadata and are cheap to regenerate).

## Skills

Read before working in the corresponding area; keep reusable verification here:

- `.agents/skills/measure-performance/SKILL.md`: required for hot-path changes and
  performance claims; harness selection, saved baselines and interleaved comparisons.
- `.agents/skills/incremental-build/SKILL.md`: required for new inputs/outputs or
  reuse changes; dependency tracking, cache publication and portable-cache verification.
