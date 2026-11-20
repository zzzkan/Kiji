# Kiji

A .NET static site generator: Razor components rendered with Blazor's HtmlRenderer.

## How rules work here

Agents copy the code around them more than they read prose, so quality comes from the
environment they work in, not from more instructions or harder review. Rules are placed at
the most reliable level that can hold them, in this order:

1. **Codebase.** API design and architecture make the wrong thing impossible or awkward: one
   way to do each task, internals not reachable, no workarounds or old ways left to copy.
2. **Static checks.** Compiler, types, analyzers, `.editorconfig` severities, tests and CI fail
   the build, with messages that say how to fix the root cause.
3. **Verification.** The site is run the way an author runs it, and the evidence is kept.
4. **Prose.** This file and the skills hold only what needs judgment.
5. **Human review** reads the evidence from levels 2 and 3, not every line of the diff.

- A correction is not a new line here. When a human corrects an agent, a mistake recurs,
  or a rule below is broken, use the `correct` skill: encode it at the highest level that
  works, prove the check fails on the real mistake, and update the rule table.
- Keep the tree free of what should not be copied: delete old ways, dead code and
  workarounds in the change that replaces them. Fix root causes, not symptoms.
- For a pattern that already exists, block new occurrences first, then clean up the old ones.
  Baselines only go down.
- No new comments except public API XML docs, license headers, facts about external
  constraints we cannot change, tool-required justifications and script usage lines. Meaning
  goes into names, types, tests and checks. See the `no-comments` skill.
- A change to user-visible behavior is not done until the `verify-site` skill has driven it
  through `dotnet publish` or `dotnet watch` and the evidence (commands, output, files,
  screenshots) is in the report or pull request.
- Automate a loop (agents merging their own changes, generated fix pull requests) only
  after its corrections are checks and its results are verified. Automating a loop that
  produces bad code produces more of it.

### Rule table

Each rule names what enforces it. `judgment` rules are prose on purpose; when one is broken
twice, encode it. `none` is debt: encode it the next time it matters. Remove prose rules
below once a check covers them.

| Rule | Enforced by |
| --- | --- |
| Warnings and recommended .NET analyzers fail the build | `Directory.Build.props` (`TreatWarningsAsErrors`, `AnalysisLevel`, `EnforceCodeStyleInBuild`) |
| File-scoped namespaces | `.editorconfig` IDE0160 at `error` |
| Public API stays compatible, except listed breaks | Package validation against 0.1.0, `src/Kiji/CompatibilitySuppressions.xml` |
| ReadyToRun images have third-party notices | Pack error in `src/Kiji/Kiji.csproj` |
| Incremental output equals a clean build | `IncrementalBuildTests` |
| The package works through the SDK in a real consumer | `Kiji.PackageTests` in CI |
| Docs site builds | CI `Build docs` step |
| User-visible changes are driven before they are called done | `verify-site` skill (judgment) |
| No new comments outside the allowed kinds | none. Baseline: 606 `//` lines in tracked `.cs` files (2026-11-20); see `no-comments` |
| One type per file | none |
| Kiji reads environment variables through IConfiguration | `src/Kiji/BannedSymbols.txt` (RS0030) |
| Kiji never sets `Environment.ExitCode` | `src/Kiji/BannedSymbols.txt` (RS0030) |
| Library components are hand-written `ComponentBase` classes, no `.razor` in `src/Kiji` | none |
| `src/Kiji.SyntheticSite` workload stays frozen | none |
| `docs/content/api-*` pages follow author-facing API changes | judgment |
| Design and constraint decisions below | judgment |

## Commands and layout

- `dotnet build -c Release` and `dotnet test -c Release` cover the whole solution.
  Pass no extra flags to test: unsupported flags reach Microsoft.Testing.Platform.
- `dotnet publish docs -c Release -o docs/dist` generates docs;
  `dotnet watch --project docs` serves it. Docs references the local Kiji project
  and imports its publish targets. Use an isolated consumer of the packed local
  package to verify package integration.
- `src/Kiji` is the single library package, including Markdown, images, feeds and hosting.
  `src/Kiji.Runtime` only packs its per-runtime images (`Kiji.Runtime.<rid>`) during Kiji's pack.
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
  started by the current task (`verify-site`'s `dev-server.sh stop` does this).

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
- One type per file. Library components and library test components are hand-written ComponentBase classes;
  docs and the package consumer fixture use .razor files and the Razor SDK.
- Update the `docs/content/api-*` pages (one per namespace) for author-facing API changes.
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
  Execution paths are fixed singletons at CreateBuilder. Content loads through
  factories/loaders/artifacts when execution starts, not while building the host.
  IHostEnvironment is read-only: its name selects the configuration files and its root the
  paths at creation, so the provider factory rejects changes. Publish takes the name
  only from DOTNET_ENVIRONMENT, the generic host convention and the one machine-dependent input
  kept, and logs it; serving follows WebApplicationBuilder's order (--environment,
  DOTNET_ENVIRONMENT, ASPNETCORE_ENVIRONMENT). Default configuration sources stay
  project-local (no environment variables, command line or user secrets, which differ between
  machines). The dev server uses standard ASP.NET Core configuration with the same project
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
  short: no serializer or reflection where a reader or writer does (the manifest is written
  by `BuildManifestSerializer` in the serializer's own format, which tests compare); no
  static initializer that loads what a site may not use; independent inputs load together
  (asset manifest, code identity and content; page entries, input verification and
  artifacts). `MarkdownWarmup` prepares the Markdown and YAML code on a spare processor
  while the host is built. It must stay free of side effects and run the default pipeline.
- A site builds for one runtime identifier (KIJI1007 otherwise; `UseCurrentRuntimeIdentifier`).
  Kiji's package holds IL and a `runtime.json` that makes NuGet restore, for that runtime,
  `Kiji.Runtime.<rid>` and SkiaSharp's Linux native assets (SkiaSharp itself brings Windows
  and macOS). Projects here that run Kiji through a project reference reference the Linux
  assets themselves. `Kiji.Runtime.<rid>` carries ReadyToRun images of Kiji and the exact
  Markdig and YamlDotNet versions Kiji references, as native assets under
  `runtimes/<rid>/native/kiji-r2r/<id>/<version>`; `Kiji.targets` removes them from the
  native assets and copies each in place of the IL only when package id and version match
  (`KijiReadyToRunDependencies=false` keeps the IL). Runtimes fall back through the graph
  (linux-musl-x64 to linux-x64, whose images are identical). Images keep the IL's metadata
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
renderer (no difference), frozen dictionaries for per-snapshot lookups (building one costs
more than a publish's lookups save), disabling DI's dynamic engine or validation on build
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
- `.agents/skills/no-comments/SKILL.md`: required when code would gain a comment or when
  cleaning existing ones.
- `.agents/skills/measure-performance/SKILL.md`: required for hot-path changes and
  performance claims; harness selection, saved baselines and interleaved comparisons.
- `.agents/skills/incremental-build/SKILL.md`: required for new inputs/outputs or
  reuse changes; dependency tracking, cache publication and portable-cache verification.
