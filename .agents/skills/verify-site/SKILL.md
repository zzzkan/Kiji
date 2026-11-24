---
name: verify-site
description: Prove a Kiji change works the way a site author uses it, by publishing and serving a real Kiji site (docs/ by default) and capturing evidence. Use after a change to user-visible behavior, before claiming it works, and to produce the evidence a pull request carries.
---

Drive a real site for what `Kiji.Tests`, `Kiji.PackageTests` and CI's docs publish do not
show; add a test when a journey keeps being driven by hand.

- Publish: `dotnet publish docs -c Release -o artifacts/verify/<run>/site`; the run is done
  when `Kiji.Build` reports `Generated N pages`. A publish to a new directory still reuses
  `.kiji/cache`; `dotnet clean docs -c Release` first when a journey needs a full render.
- Serve: `dotnet pwsh .agents/skills/verify-site/scripts/dev-server.ps1 start|doctor|stop
  [project] [url]` (default `http://127.0.0.1:8080`). It is ready when the log says
  `Dev server started`; restart instead of driving a server `doctor` rejects. `stop` kills the
  whole process tree it started: stopping only `dotnet watch` leaves an inner process that
  restarts the site, so never stop servers by name.
- Pages live under BaseUrl's path: `/docs/...` on the docs site.
- Browser: `node .agents/skills/verify-site/scripts/screenshot.mjs <url> <out.png> [w] [h]`
  exits 1 on an HTTP error, a script error or a failed same-origin request. The docs site's
  CDN syntax highlighter may fail in a sandbox; that is not a Kiji failure.
- Content edits: copy the file into the run directory, append a unique marker, poll once a
  second until it appears, then restore it and poll until it is gone.
- Evidence comes only from the author path (`dotnet publish`, `dotnet watch`, HTTP, a
  browser), recorded as command plus output under `artifacts/verify/<run>/`. Name a journey
  that cannot run here instead of substituting a weaker one; a journey that fails on
  unchanged `main` is a product bug to report.
- Finish with `stop`, restored files and a clean `git status --short`, after failures too.
