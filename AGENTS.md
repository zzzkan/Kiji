# Kiji

A .NET static site generator: Razor components rendered with Blazor's HtmlRenderer.

## Repository conventions

Project boundaries and packaging are defined in `Directory.Build.targets`. Compiler
settings live in `Directory.Build.props` and `.editorconfig`, and repository analyzers in
`tools/Kiji.Analyzers`. The docs project is a usage example.

Prefer names, types and tests that explain the code. In compiled C#, KR0004 permits XML
documentation on the public library API. In scripts and other languages, omit explanatory
comments when the code conveys the same information. Keep directives that tools consume.

Update the corresponding `docs/content/api-*` page when the author-facing API changes.

## Commands and layout

- `dotnet build -c Release` and `dotnet test -c Release` cover the whole solution.
  Pass no extra flags to test: unsupported flags reach Microsoft.Testing.Platform.
- Run scripts with `dotnet tool restore` once, then `dotnet pwsh <script>.ps1`; CI steps use
  `shell: pwsh`.
- `dotnet publish docs -c Release -o docs/dist` generates docs;
  `dotnet watch --project docs` serves it. Docs references the local Kiji project and
  imports its publish targets. Use an isolated consumer of the packed local
  package to verify package integration.
- `Kiji.Core` holds the model, generation, rendering, routing, assets and builder state;
  `Kiji.Hosting` (publish, dev server), `Kiji.Markdown`, `Kiji.Images.Skia`, `Kiji.Feeds` and
  `Kiji.Sitemaps` build on it; `src/Kiji` holds `StaticSite`, wires the defaults and packs them
  all. A project reference to Kiji brings every `src` library, as the package does.
  `src/Kiji.Runtime` only packs per-runtime images during Kiji's pack;
  `src/Kiji/MSBuild/Kiji.targets` integrates publish, watch and clean.
- Public namespaces are what an author imports, one per feature: `Kiji` (site, pages, content,
  builder), `Kiji.Images` (the image processor and its options), `Kiji.Markdown`, `Kiji.Feeds`,
  `Kiji.Sitemaps`. Internal namespaces in `Kiji.Core`, from
  the bottom: `Kiji.Paths`, `Kiji.Diagnostics`, `Kiji.Incremental` (fingerprints, dependency
  recording, manifest, caches, code identity), `Kiji.Definition` (what the builder records),
  `Kiji.Rendering`, `Kiji.Routing`, `Kiji.Components`, `Kiji.StaticAssets`, then the image
  pipeline in `Kiji.Images` and `Kiji.Generation` on top. Other projects use their own namespace
  (`Kiji.Hosting`, `Kiji.Images.Skia` for the default SkiaSharp processor, ...).
- Each `src` library project has `tests/<Project>.Tests`; `tests/Kiji.Testing` holds helpers
  they share. A test belongs to the lowest project it can reference; `tests/Kiji.Tests` holds
  the tests that run a whole site through `StaticSite`, with the TestSite fixtures. Classes run
  in parallel; library tests do not pack or build consumers.
- `tests/Kiji.PackageTests` contains serial, black-box package scenarios with one fixed
  consumer workspace. External dependencies use NuGet's global cache; only Kiji
  is isolated. See its README for execution and cleanup.
  Run `dotnet test -c Release` from a test project to select that level.
- `benchmarks/Kiji.SyntheticSite` is the frozen end-to-end workload, run by the code in its
  `Harness/` folder; `benchmarks/Kiji.Benchmarks` contains BenchmarkDotNet measurements.
- Stop any verification server/watch process before completion. Stop only processes
  started by the current task (`verify-site`'s `dev-server.ps1 stop` does this).

## Design

This section and the next record design decisions that need judgment. When a change breaks
one, the `correct` skill decides whether it can become a check.

- Follow the .NET host builder shape: CreateBuilder -> builder.Services/Add*/Use* -> Build
  -> RunAsync. StaticSite.CreateBuilder returns HostApplicationBuilder; Kiji registrations are
  IHostApplicationBuilder extensions stored in DI. Publish and serve are hosted services that
  wait for ApplicationStarted. Add* adds, Use* sets a single value.
  Kiji adds no command-line options:
  watch serves, publish generates, clean removes the caches and staging directory.
  ASP.NET Core configuration owns the dev server address. Serving reads only the standard
  --environment host argument. Prefer conventions to flags.
- All log output goes through the site's ILogger (Kiji's own under Kiji.Build), except the dev
  server's watch-style status lines and the runtime's unhandled exception report. Publish uses
  the standard console formatter; MSBuild receives every line as a high-importance message and
  ignores its error/warning format. Only the exit code fails a publish: failed or canceled runs
  make RunAsync throw from the hosted service's StopAsync (avoiding duplicate host logs). Log
  levels come from the Logging section, not MSBuild properties.
- Dev and publish share SiteEngine.RenderPageAsync. Never emit stale output;
  ambiguous incremental state requires rendering.
- Docs and the package consumer fixture use .razor files and the Razor SDK.
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
  Execution paths are fixed singletons at CreateBuilder. Content loads through
  factories/loaders/artifacts when execution starts, not while building the host.
  IHostEnvironment is read-only: its name selects the configuration files and its root the
  paths at creation, so the provider factory rejects changes. Publish takes the name
  only from DOTNET_ENVIRONMENT, the generic host convention and the one machine-dependent input
  kept, and logs it; serving follows WebApplicationBuilder's order (--environment,
  DOTNET_ENVIRONMENT, ASPNETCORE_ENVIRONMENT). The dev server uses standard ASP.NET Core configuration with the same project
  directory and environment. Per-key reads are not tracked, so the environment name and all
  configuration outside the Logging and Metrics sections are in the publish options
  fingerprint.
- The dev server enforces BaseUrl's path prefix and uses `.kiji/dev-site`.
  Only `.kiji/cache` (pages) and `.kiji/jit` (publish JIT profile) are portable; CI restores
  either or both. Planning creates no output directories; a page creates its own when it
  writes. When the output directory does not exist as a build begins (clean checkout,
  `dotnet clean`), every file in it comes from that build: pages write without comparing,
  read their own stamps, and reconciliation reads the rest one file at a time instead of
  listing the tree. Stamps from an earlier manifest are cleared then.
- A publish is one short process whose time is mostly first use of code. Keep its cold path
  short: no reflection where a reader or writer does (the manifest is written by
  `BuildManifestSerializer` in the serializer's own format, which tests compare); no
  static initializer that loads what a site may not use; independent inputs load together
  (asset manifest, code identity and content; page entries, input verification and
  artifacts). `MarkdownWarmup` prepares the Markdown and YAML code on a spare processor
  while the host is built. It must stay free of side effects and run the default pipeline.
- A site builds for one runtime identifier (KIJI1007 otherwise; `UseCurrentRuntimeIdentifier`).
  Kiji's package holds IL and a `runtime.json` that makes NuGet restore, for that runtime,
  `Kiji.Runtime.<rid>` and SkiaSharp's Linux native assets (SkiaSharp itself brings Windows
  and macOS). Projects here that run Kiji through a project reference reference the Linux
  assets themselves. `Kiji.Runtime.<rid>` carries ReadyToRun images of Kiji's assemblies and the exact
  Markdig and YamlDotNet versions Kiji references, as native assets under
  `runtimes/<rid>/native/kiji-r2r/<id>/<version>`; `Kiji.targets` removes them from the
  native assets and copies each in place of the IL only when package id and version match
  (`KijiReadyToRunDependencies=false` keeps the IL). Runtimes fall back through the graph
  (linux-musl-x64 to linux-x64, whose images are identical). Kiji's own assemblies are
  compiled as one version bubble, since they ship and load together, so calls and generic
  instantiations across them keep precompiled code. Images keep the IL's metadata
  and MVID, so code identity and caches are unchanged. Pack fails until
  `THIRD-PARTY-NOTICES.txt` names the versions the images are compiled from.
  `KijiReadyToRun=false` or a Debug pack makes Kiji's package alone. The images are compiled
  at pack time against each runtime pack, so restoring Kiji downloads those packs and the
  compiler. Partial images from a MIBC profile need dotnet-pgo, which nuget.org does not
  carry; each runtime package is about 1.4 MB.
- HostApplicationBuilder.Services is standard DI; KijiServiceProviderFactory validates
  Kiji-owned registrations (including its own hosted service), records code assemblies and
  enables scope validation in Build. A replaced factory is rejected at startup.
  Kiji's defaults and managed services are registered
  when the builder is created. Each render owns a scope. Loaders, page parameter/feed
  factories and artifacts use the root provider and cannot resolve scoped services.
  User hosted services' state is untracked and may require AlwaysRender.
  Compute derived data during rendering or in scoped services;
  content reload does not recreate singletons. Use AlwaysRender on components reading
  untracked external state. Content items are tracked by source, ID and file; values assigned
  to them after loading are untracked state, documented rather than detected.
  Instantiating any ErrorBoundaryBase component prevents HTML reuse for that page,
  even when healthy. The default boundary logger reports exceptions as warnings;
  logging never controls cacheability or the publish result. Use an unhandled exception
  for a build gate.
- Use Blazor's PageTitle and HeadContent; KijiRoot supplies the single HeadOutlet.
  Each page gets a fresh renderer/dispatcher, isolating the framework's section state.
  HeadContent replaces earlier providers rather than appending; compose shared tags
  inside the page's HeadContent. Await quiescence before serializing the document.
- Responsive images live under `_images/`, independent of page routes, at immutable
  content-derived names, so carried and freshly rendered pages never overwrite each
  other's variants, including with custom processors. Record shared outputs on every
  referring page. Sources resolve within registered, watched content roots, including
  external and linked roots, as Markdown does; reject symbolic links or junctions below
  those roots.
- Customize IImageProcessor through builder.Services. The last unkeyed registration must
  be a singleton; standard DI ownership applies. It must support concurrency and retain
  no page/content state. Reload does not recreate it. Include external configuration
  and encoder versions in cache identities.
  Every ImageProcessorOptions setting must stay in its CacheIdentity. Options are validated when
  the site starts running, never in Build.

Do not reintroduce evaluated alternatives: pooled HtmlRenderer (root/scope state cannot
reset), RouteView (PageView owns Kiji's layout resolution), AOT/trimming (reflection),
source-generated page discovery (cached scanning suffices), IBufferWriter rendering
(HtmlRootComponent exposes TextWriter), page-bundle image variants (articles could not
share an image), incremental RSS/sitemap (depend on all metadata and are cheap to
regenerate), or a frozen configuration snapshot (default sources do not reload during a
publish). For the cold publish process, measured and rejected: tiering settings
(`TieredCompilation`, `QuickJit`, `QuickJitForLoops`, `CallCountingDelayMs`,
`CallCountThreshold`: compiling optimized code costs more than a short process gets back),
non-concurrent GC and a fixed gen0 budget (no difference), reading code identity from
assembly files with MetadataReader (slower than loading them), collecting code identity
while the host is built (contends with the host's own loading), warming the component
renderer (no difference), disabling DI's dynamic engine or validation on build
(no difference), persisting pages on dedicated writer threads (no gain on Windows, much
slower on ext4), skipping the Markdown warm-up while a profile replays (slower), and disabling W^X (`DOTNET_EnableWriteXorExecute=0`, faster but
removes a runtime mitigation; a site owner can set it, Kiji does not).

## Skills

Read before working in the corresponding area; keep reusable verification here:

- `.agents/skills/correct/SKILL.md`: required when a human corrects an agent, a mistake
  recurs, a rule here is broken, or a rule is added; turns it into a check.
- `.agents/skills/verify-site/SKILL.md`: required before calling a user-visible change done;
  drives a real site and records evidence. Its `features/` map is the shared vocabulary for
  Kiji's features.
- `.agents/skills/measure-performance/SKILL.md`: required for hot-path changes and
  performance claims; harness selection, saved baselines and interleaved comparisons.
- `.agents/skills/incremental-build/SKILL.md`: required for new inputs/outputs or
  reuse changes; dependency tracking, cache publication and portable-cache verification.
