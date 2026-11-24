# Kiji

A .NET static site generator: Razor components rendered with Blazor's HtmlRenderer.

## Repository conventions

Repository rules are checks: project layout, references, packages and the Kiji package in
`Directory.Build.targets`, compiler settings in `Directory.Build.props` and `.editorconfig`,
banned APIs in `BannedSymbols*.txt`, analyzers in `tools/Kiji.Analyzers`, and the rest in
`tests/Kiji.RepositoryTests`. Each failure message says what to do. The docs project is a
usage example outside these checks.

Prefer names, types and tests that explain the code. In scripts and other languages, omit
explanatory comments when the code conveys the same information.

## Commands and layout

- `dotnet build -c Release` and `dotnet test -c Release` cover the whole solution.
  Pass no extra flags to test: unsupported flags reach Microsoft.Testing.Platform.
  Run `dotnet test -c Release` from a test project to select that level.
- Run scripts with `dotnet tool restore` once, then `dotnet pwsh <script>.ps1`.
- `dotnet publish docs -c Release -o docs/dist` generates docs;
  `dotnet watch --project docs` serves it. Docs references the local Kiji project, so it runs
  IL; use an isolated consumer of the packed package to verify package integration.
- `src/Kiji` holds `StaticSite`, wires the defaults and packs every `src` library into the one
  Kiji package; a project reference to Kiji brings them all. `src/Kiji/MSBuild/Kiji.targets`
  integrates publish, watch and clean.
- A test belongs to the lowest project it can reference; `tests/Kiji.Tests` runs whole sites
  through `StaticSite`. Test classes run in parallel; library tests do not pack or build
  consumers. `tests/Kiji.PackageTests` runs serial black-box scenarios against the packed
  package; see its README for execution and cleanup.
- `benchmarks/Kiji.SyntheticSite` is the frozen end-to-end workload, run by its `Harness/`.
- Stop any verification server/watch process you started before completion, and only those
  (`verify-site`'s `dev-server.ps1 stop` does this).

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
- Publish logs with the standard console formatter; MSBuild receives every line as a
  high-importance message and ignores its error/warning format. Only the exit code fails a publish: failed or canceled runs
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
- A site builds for one runtime identifier (KIJI1007 otherwise; `UseCurrentRuntimeIdentifier`),
  so the package's `runtime.json` restores exactly one `Kiji.Runtime.<rid>` with ReadyToRun
  images of Kiji's assemblies and of the Markdig and YamlDotNet versions Kiji references.
  `Kiji.targets` swaps an image in only when package id and version match. Kiji's assemblies
  are compiled as one version bubble because they ship and load together. Images keep the
  IL's metadata and MVID, so code identity and caches are unchanged. Partial images from a
  MIBC profile need dotnet-pgo, which nuget.org does not carry.
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
reset), AOT/trimming (reflection), source-generated page discovery (cached scanning
suffices), IBufferWriter rendering (HtmlRootComponent exposes TextWriter), page-bundle image
variants (articles could not share an image), incremental RSS/sitemap (depend on all metadata
and are cheap to regenerate), or a frozen configuration snapshot (default sources do not
reload during a publish). For the cold publish process, measured and rejected: reading code
identity from assembly files with MetadataReader (slower than loading them), collecting code
identity while the host is built (contends with the host's own loading), warming the
component renderer, disabling DI's dynamic engine or validation on build (no difference),
persisting pages on dedicated writer threads (no gain on Windows, much slower on ext4) and
skipping the Markdown warm-up while a profile replays (slower). Runtime settings for the
publish process are listed, with the rejected ones, in `PublishRuntimeSettingsTests`.

## Skills

Read before working in the corresponding area; keep reusable verification here:

- `.agents/skills/correct/SKILL.md`: required when a human corrects an agent, a mistake
  recurs, a rule here is broken, or a rule is added; turns it into a check.
- `.agents/skills/verify-site/SKILL.md`: required before calling a user-visible change done;
  drives a real site and records evidence.
- `.agents/skills/measure-performance/SKILL.md`: required for hot-path changes and
  performance claims; harness selection, saved baselines and interleaved comparisons.
- `.agents/skills/incremental-build/SKILL.md`: required for new inputs/outputs or
  reuse changes; dependency tracking, cache publication and portable-cache verification.
