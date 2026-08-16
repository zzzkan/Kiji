---
title: Hello, Kiji
description: The first post on your new site.
createdAt: 2026-01-01
---

This file is `contents/hello-world/index.md`. The directory name is the slug, so the page
is published at `/hello-world/`.

Add another directory under `contents/` with an `index.md` in it and it shows up on the
home page. The fields at the top are YAML front matter — Kiji does not define their shape,
so edit `PostFrontMatter.cs` to add whatever you need.

Put an image beside this file and reference it by name to get responsive WebP variants:

```markdown
![A description](photo.jpg)
```
