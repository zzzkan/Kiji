# Kiji

A .NET static site generator: Razor components rendered with Blazor's HtmlRenderer.

## Working here

- Pass no extra flags to `dotnet test`: they reach Microsoft.Testing.Platform. Run it from a
  test project to select that level.
- Scripts are PowerShell run with `dotnet tool restore` once, then `dotnet pwsh <script>.ps1`.
  No explanatory comments in scripts either.
- `docs/` and the package consumer fixture are author-style usage examples (.razor files, the
  Razor SDK) outside the repository checks. Docs references the local Kiji project, so it runs
  IL; ReadyToRun and restore behavior need a consumer of the packed package.
- A test belongs to the lowest project it can reference. Library tests do not pack or build
  consumers; `tests/Kiji.PackageTests` does, serially (see its README).
- Tests must catch realistic regressions, not repeat library guarantees or what compilation
  and pack already check. Experiments and reports go in the ignored `artifacts/`.

## Design

- Follow the .NET host builder shape: CreateBuilder -> builder.Services/Add*/Use* -> Build
  -> RunAsync. StaticSite.CreateBuilder returns HostApplicationBuilder; Kiji registrations are
  IHostApplicationBuilder extensions stored in DI. Publish and serve are hosted services that
  wait for ApplicationStarted. Add* adds, Use* sets a single value.
- Prefer conventions to flags. Kiji adds no command-line options: watch serves, publish
  generates, clean removes the caches and staging directory. ASP.NET Core configuration owns
  the dev server address; serving reads only the standard --environment host argument.
- Every `src` library ships in the one Kiji package.
- Publish logs with the standard console formatter; MSBuild receives every line as a
  high-importance message and ignores its error/warning format. Only the exit code fails a
  publish: failed or canceled runs make RunAsync throw from the hosted service's StopAsync
  (avoiding duplicate host logs). Log levels come from the Logging section, not MSBuild
  properties.
- Dev and publish share SiteEngine.RenderPageAsync. Never emit stale output; ambiguous
  incremental state requires rendering.

## Constraints worth preserving

- Discover all routes in the entry assembly automatically; other assemblies require
  AddPageAssembly. Routes allow literals and simple parameters only. Parameterized routes
  require AddPageParameters; an empty sequence generates no pages. Each entry is a whole
  parameter set rendered at every parameterized route; fixed routes beside them keep default
  rendering, while components with only fixed routes receive the sets. Supplied values must
  name writable [Parameter] properties; do not enforce EditorRequired. UseNotFoundPage still
  requires its own route and a registered page assembly.
- MSBuild supplies the project directory through runtimeconfig. Execution paths are fixed at
  CreateBuilder. Content loads through factories/loaders/artifacts when execution starts, not
  while building the host. IHostEnvironment is read-only: its name selects the configuration
  files and its root the paths. Publish takes the name only from DOTNET_ENVIRONMENT (the one
  machine-dependent input kept) and logs it; serving follows WebApplicationBuilder's order.
  Per-key configuration reads are not tracked, so the environment name and all configuration
  outside the Logging and Metrics sections are in the publish options fingerprint.
- A publish is one short process whose time is mostly first use of code. Keep its cold path
  short: no reflection where a reader or writer does, no static initializer that loads what a
  site may not use, and independent inputs load together (asset manifest, code identity and
  content; page entries, input verification and artifacts). `MarkdownWarmup` prepares the
  Markdown and YAML code on a spare processor while the host is built; it must stay free of
  side effects and run the default pipeline.
- A site builds for one runtime identifier, so the package restores exactly one
  `Kiji.Runtime.<rid>` with ReadyToRun images of Kiji's assemblies and of the Markdig and
  YamlDotNet versions Kiji references. Kiji's assemblies are one version bubble because they
  ship and load together. Images keep the IL's metadata and MVID, so code identity and caches
  are unchanged.
- Each render owns a DI scope. Loaders, page parameter/feed factories and artifacts use the
  root provider and cannot resolve scoped services. Content reload does not recreate
  singletons, so compute derived data during rendering or in scoped services. Use
  AlwaysRender on components reading untracked external state: user hosted services' state
  and values assigned to content items after loading (documented, not detected).
  Instantiating any ErrorBoundaryBase prevents HTML reuse for that page, even when healthy.
  Logging never controls cacheability or the publish result; use an unhandled exception for a
  build gate.
- Use Blazor's PageTitle and HeadContent; KijiRoot supplies the single HeadOutlet. Each page
  gets a fresh renderer/dispatcher, isolating the framework's section state. HeadContent
  replaces earlier providers rather than appending; compose shared tags inside the page's
  HeadContent. Await quiescence before serializing the document.
- Responsive images live under `_images/`, independent of page routes, at immutable
  content-derived names, so carried and freshly rendered pages never overwrite each other's
  variants, including with custom processors. Sources resolve within registered, watched
  content roots, including external and linked roots, as Markdown does; reject symbolic links
  or junctions below those roots.
- Customize IImageProcessor through builder.Services. The last unkeyed registration must be a
  singleton; it must support concurrency and retain no page/content state, and reload does not
  recreate it. Include external configuration and encoder versions in cache identities; every
  ImageProcessorOptions setting stays in its CacheIdentity. Options are validated when the
  site starts running, never in Build.

## Skills

Read the skill before working in its area:

- `.agents/skills/correct`: a correction, a repeated mistake or a rule change.
- `.agents/skills/verify-site`: before calling a user-visible change done.
- `.agents/skills/measure-performance`: hot-path changes and performance claims.
- `.agents/skills/incremental-build`: new build inputs/outputs or reuse changes.
