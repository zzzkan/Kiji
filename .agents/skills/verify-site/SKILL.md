---
name: verify-site
description: Prove a Kiji change works the way a site author uses it, by publishing and serving a real Kiji site (docs/ by default) and capturing evidence. Use after a change to user-visible behavior, before claiming it works, and to produce the evidence a pull request carries.
---

Tests prove what they assert; this skill shows the product. Automated coverage comes first:
`Kiji.Tests` (whole sites through `StaticSite`, the dev server), `Kiji.PackageTests`
(publish, edit, clean, watch and reload through the packed package) and CI's docs publish
(a no-change republish renders no pages). Drive a real site for what those do not show, and
add a test when a journey keeps being driven by hand.

## Launch

- Publish: `dotnet publish docs -c Release -o artifacts/verify/<run>/site`. Ready when it
  exits 0 and `Kiji.Build` reports `Generated N pages`. `docs/` runs the working tree as IL;
  ReadyToRun and restore behavior need the packed package (`Kiji.PackageTests`).
- Serve: `dotnet pwsh .agents/skills/verify-site/scripts/dev-server.ps1 start [project] [url]`
  (default `http://127.0.0.1:8080`; it refuses an address already answering). Ready means
  the log says `Dev server started`. `doctor` checks the process, `GET /` and the log
  without changing anything; restart instead of driving a server doctor rejects.

## Drive and record

- HTTP: `curl -s -o <file> -w '%{http_code}' <url>`; pages live under BaseUrl's path
  (`/docs/...` for the docs site).
- Browser: `node .agents/skills/verify-site/scripts/screenshot.mjs <url> <out.png> [w] [h]`
  prints status, title, script errors and failed requests, and exits 1 on an HTTP error, a
  script error or a failed same-origin request.
- Content edits: copy the file to the run directory, append a unique marker, poll once a
  second until it appears, restore the copy and poll until it is gone.
- Record each claim with the exact command and its output, under `artifacts/verify/<run>/`.
  Use only the author path (`dotnet publish`, `dotnet watch`, HTTP, a browser); mock nothing.
  Name any journey that cannot run here instead of substituting a weaker one.

## Cleanup

`dev-server.ps1 stop [project] [url]` kills only the process tree it started. Restore edited
files and confirm with `git status --short`, after failed drives too.
