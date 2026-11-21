---
name: no-comments
description: What to do instead of writing a comment. Use when KR0004 or CS1591 fails, or when code seems to need an explanation.
---

KR0004 fails the build on any comment (C#, MSBuild, YAML, script, CSS, JS, Razor) and on XML
docs outside the public API of `src` library projects. CS1591 fails on undocumented public
API there. A new kind of file reaches the analyzer through `AdditionalFiles` in
`Directory.Build.targets`.

Instead of a comment:

- **What the code does**: rename, extract a method, or introduce a type until it reads clearly.
- **A rule or constraint** ("must", "never", "keep in sync"): make it a check with the
  `correct` skill: a type, an analyzer, a banned API, a project reference, or a test whose
  name states the requirement.
- **A workaround**: fix the cause, or report it as open.
- **Commented-out code**: delete it.
- **A suppression's reason**: `[SuppressMessage(..., Justification = "...")]` on the
  narrowest member, never a `#pragma` with a comment.

Public API docs in `src` library projects are one-line `<summary>`s stating what an author can rely on;
add `<param>`, `<exception>` or `<remarks>` only when the contract needs them.
