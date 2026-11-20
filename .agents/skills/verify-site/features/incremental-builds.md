# Incremental builds

## Sub-features

- A publish with no changes renders no pages and copies no static files.
- Editing one Markdown file re-renders the pages that read it, and no others.
- Restoring the file brings the output back to its earlier content.
- `.kiji/cache` is portable between checkouts (covered by the incremental-build skill's
  `Verify-PortableCache.ps1`).

## How to get to it (user POV)

The author publishes again after a change, locally or in CI with `.kiji/cache` restored.

## Driving it with dotnet publish

```sh
O=artifacts/verify/<run>/site
dotnet publish docs -c Release -o $O | grep -A1 'Kiji.Build\[2\]'
dotnet publish docs -c Release -o $O | grep -A1 'Kiji.Build\[2\]'
cp docs/content/troubleshooting/index.md artifacts/verify/<run>/orig.md
printf '\nVERIFY-EDIT\n' >> docs/content/troubleshooting/index.md
dotnet publish docs -c Release -o $O | grep -A1 'Kiji.Build\[2\]'
grep -c VERIFY-EDIT $O/docs/troubleshooting/index.html
cp artifacts/verify/<run>/orig.md docs/content/troubleshooting/index.md
dotnet publish docs -c Release -o $O | grep -A1 'Kiji.Build\[2\]'
git status --short docs
```

Expected on the docs site: the second publish reports `Generated 0 pages (19 unchanged ...)`
and `Static files: 0 copied, 8 unchanged`. Each edit publish reports
`Generated 1 pages (16 unchanged, skipped, 2 re-rendered but unchanged)`: the two pages
next to it in the navigation re-render, because they read the edited page's metadata. The
marker count is 1 after the edit and 0 after the restore. `git status` is clean.

## Gotchas

- Any change to `src/Kiji` or `docs` code changes the code identity, so every page
  renders. Measure reuse with code unchanged between the two publishes.
- Detailed reuse rules and regression tests are in the incremental-build skill.
