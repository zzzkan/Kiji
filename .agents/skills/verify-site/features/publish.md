# Publish a static site

## Sub-features

- One `index.html` per route, including parameterized routes from `AddPageParameters`.
- `404.html` from `UseNotFoundPage`.
- `sitemap.xml` from `AddSitemap`, with absolute URLs under `BaseUrl`.
- Static web assets (CSS, JS, scoped CSS bundles) copied beside the pages.
- `dotnet clean` removes `.kiji` (caches and staging).

## How to get to it (user POV)

The author runs `dotnet publish -c Release -o dist` in the site project and deploys `dist`.

## Driving it with dotnet and the file system

```sh
dotnet publish docs -c Release -o artifacts/verify/<run>/site
find artifacts/verify/<run>/site -name index.html | wc -l
grep -o '<title>[^<]*' artifacts/verify/<run>/site/docs/getting-started/index.html
grep -o '<title>[^<]*' artifacts/verify/<run>/site/404.html
head -c 400 artifacts/verify/<run>/site/sitemap.xml
dotnet clean docs -c Release && ls -a docs | grep -c '^\.kiji$'
```

Expected on the docs site: the log reports `Generated 19 pages` on a fresh publish, which is
18 routes plus the not-found page. `<title>` reads `Getting started - Kiji` and
`Not found - Kiji`; sitemap URLs start with `https://kiji-docs.zzzkan.workers.dev/`; after
clean, `.kiji` is gone (the count prints 0).

## Gotchas

- The site builds for one runtime identifier; `docs` sets `UseCurrentRuntimeIdentifier`.
- A publish to a new `-o` directory still reuses `.kiji/cache` from an earlier publish.
  Run `dotnet clean docs -c Release` first when a journey needs a full render.
- The page count changes when docs pages are added; compare against the content directory,
  not a remembered number.
