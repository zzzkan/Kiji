---
name: verify-site
description: Prove a Kiji change works the way a site author uses it, by publishing and serving a real Kiji site (docs/ by default) and capturing evidence (command output, HTTP responses, generated files, screenshots). Use after any change to user-visible behavior (publish output, dev server, Markdown, images, routing, incremental builds, MSBuild integration), before claiming such a change works, and to produce the evidence a pull request carries for review.
---

Tests prove what they assert. This skill proves the product: run a site the way an author
does and look at what they would see. Reviewers read the evidence this produces instead of
re-deriving behavior from the diff.

The features are in `features/`, one file per user-facing feature. Read the file for each
feature the change touches; drive every journey it lists.

## Launch

The surface is a .NET CLI and a local web server. The default site is `docs/`, which
references `src/Kiji` and imports its MSBuild targets, so it runs the working tree as IL.
For package behavior (ReadyToRun images, `runtime.json`, NuGet restore), use the packed
package instead: `Kiji.PackageTests` does this, and `features/package.md` covers it by hand.

- Publish: `dotnet publish docs -c Release -o artifacts/verify/<run>/site`. Ready when the
  command exits 0 and `Kiji.Build` reports `Generated N pages`.
- Serve: `dotnet pwsh .agents/skills/verify-site/scripts/dev-server.ps1 start [project] [url]`
  (run `dotnet tool restore` once for `dotnet pwsh`).
  The default address is `http://127.0.0.1:8080`. Pass another url (for example
  `http://127.0.0.1:8081`) to run beside a server you did not start; the script refuses an
  address that is already answering. Ready means the log says `Dev server started`, not
  only that a process exists.

Everything a run writes goes under `artifacts/verify/` (ignored). Keep one directory per run.

## Doctor

`dotnet pwsh .agents/skills/verify-site/scripts/dev-server.ps1 doctor [project] [url]` checks
without changing anything: the recorded process is alive, `GET /` answers, and the watch log has no
errors. Run it before the first drive, and again after any failed drive. If doctor fails and
the log does not explain it, stop and restart the server instead of driving a wedged one.

## Drive

- HTTP: `curl -s -o <file> -w '%{http_code}' <url>`. Pages live under the site's BaseUrl
  path (`/docs/...` for the docs site, `/` for its home page).
- Browser: `node .agents/skills/verify-site/scripts/screenshot.mjs <url> <out.png> [w] [h]`.
  It prints status, title, script errors and failed requests as JSON, and exits 1 on an HTTP
  error, a page script error or a failed same-origin request. Failed third-party requests
  (the docs site loads Prism from a CDN) are reported but do not fail it.
- Content edits: copy the file to the run directory first, append a unique marker, and poll
  the page until the marker appears; restore the copy and poll until it is gone. Poll once a
  second (`sleep 1`), never with a fixed long sleep.
- Publish output: read the generated files directly (`index.html` per route, `404.html`,
  `sitemap.xml`, `_images/`), and the `Kiji.Build` log lines for counts.

## Evidence

Evidence must come from the real author path: `dotnet publish`, `dotnet watch`, HTTP and a
browser. Not test-only endpoints, not a hand-built host.

- Check the action and its result: after an edit, the page shows the new text and a
  republish reports the expected page counts.
- Check side effects: files that should appear do, files that should go are gone, and the
  working tree is back to its starting state (`git status --short`).
- Record each claim with the exact command and the output it printed. Screenshots go next
  to it. A claim without its output is not evidence.
- Mock nothing. If a journey cannot run here (another OS, a blocked network host), say so
  and name the step left unverified; do not substitute a weaker journey.
- Never weaken a journey to make it pass. A journey that fails on unchanged `main` is a
  product bug: report it.

## Cleanup

- `dotnet pwsh .agents/skills/verify-site/scripts/dev-server.ps1 stop [project] [url]` stops only
  the process tree that script started. Never stop processes by name.
- Restore every content file you edited; confirm with `git status --short`.
- Remove scratch output you no longer need, but keep the evidence files the report cites.
- Run cleanup after a failed drive too.

## Helpers

- `scripts/dev-server.ps1 start|doctor|stop [project] [url]`: PowerShell (`dotnet pwsh`) on Linux,
  macOS and Windows. State goes in `artifacts/verify/dev-server-<port>/` (`process.json`,
  `watch.log`). `stop` kills the process tree (`dotnet watch`, `dotnet run`, the site) only
  while the recorded process still has the recorded start time; build nodes are not reused,
  so none outlive the server. `/bin/sh` (or `cmd.exe` on Windows) only redirects the
  server's output to the log, so it keeps writing after PowerShell exits.
- `scripts/screenshot.mjs <url> <out.png> [width] [height]`: the preinstalled Playwright and
  Chromium. It stays in JavaScript because the browser is installed for Node's Playwright;
  Playwright for .NET would download a second browser. Take screenshots a reviewer will see at the default size.

## Keep this skill true

When a change adds or alters a user-facing feature, update its file in `features/` in the
same change, and drive it once. When a journey here no longer matches the product, decide
which is wrong: fix the map for documentation drift, fix the helper for a harness gap, and
report a product regression rather than editing the map to hide it.
