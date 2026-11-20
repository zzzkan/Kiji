# Markdown content and responsive images

## Sub-features

- Markdown files with YAML front matter under `content/` become typed content items
  (`AddMarkdownContent`).
- Images referenced from Markdown become `<img>` elements with `srcset` variants under
  `_images/`, at content-derived immutable names.

## How to get to it (user POV)

The author adds `content/<slug>/index.md` with front matter and an image beside it, then
publishes or serves the site.

## Driving it with dotnet publish and the file system

```sh
O=artifacts/verify/<run>/site
dotnet publish docs -c Release -o $O
grep -o '<img[^>]*srcset="[^"]*"' $O/docs/getting-started/index.html | head -1
ls $O/_images | head
```

Expected: `src` and `srcset` point at `/_images/<name>.<hash>.<width>w.webp` and every
referenced file exists in `$O/_images`.

## Gotchas

- Image variants are content-addressed: editing an image produces new names, and old ones are
  removed during reconciliation, not overwritten.
- Images outside registered content roots, or reached through symbolic links below them,
  are rejected.
- The docs site loads its syntax highlighter from a CDN; a sandbox that blocks the CDN shows
  unhighlighted code, which is not a Kiji failure.
