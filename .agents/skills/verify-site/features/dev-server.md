# Dev server and live reload

## Sub-features

- `dotnet watch` serves the site from source at `http://127.0.0.1:8080` unless ASP.NET Core
  configuration (`ASPNETCORE_URLS`, launch settings) names another address.
- Requests outside `BaseUrl`'s path prefix return 404.
- Markdown edits, additions and deletions update served pages without a restart.
- Hosts other than the configured ones are rejected (400).
- Razor, CSS and JS edits reach the browser with one reload (covered by `Kiji.PackageTests`).

## How to get to it (user POV)

The author runs `dotnet watch` in the site project and opens the printed address.

## Driving it with dev-server.ps1, curl and Playwright

```sh
S="dotnet pwsh .agents/skills/verify-site/scripts/dev-server.ps1"
$S start docs http://127.0.0.1:8081 && $S doctor docs http://127.0.0.1:8081
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8081/docs/routing/
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8081/docs/nope/
curl -s -o /dev/null -w '%{http_code}\n' -H 'Host: evil.example' http://127.0.0.1:8081/
node .agents/skills/verify-site/scripts/screenshot.mjs http://127.0.0.1:8081/docs/getting-started/ artifacts/verify/<run>/getting-started.png
$S stop docs http://127.0.0.1:8081
```

Expected: 200, 404, 400. A Markdown edit (see SKILL.md, Drive) shows on the page within a
few seconds and disappears after the file is restored.

## Gotchas

- The first start builds the site in Debug and takes tens of seconds; `start` waits up to
  180 s for `Dev server started`.
- Signalling only `dotnet watch` leaves its inner `dotnet-watch` process running, and that
  one restarts the site on the same port. `dev-server.ps1 stop` kills the whole tree; never
  stop a server by process name.
