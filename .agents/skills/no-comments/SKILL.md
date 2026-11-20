---
name: no-comments
description: What to do instead of writing a comment. Use when a check reports a comment (NoCommentTests, KR0004, CS1591) or when code seems to need an explanation.
---

The repository has no comments. `NoCommentTests` fails on any C#, MSBuild, YAML, script,
CSS, JS or Razor comment; KR0004 fails on XML docs outside a packed project's public API;
CS1591 fails on undocumented public API in `src/Kiji`.

Instead of a comment:

- **What the code does**: rename, extract a method, or introduce a type until it reads clearly.
- **A rule or constraint** ("must", "never", "keep in sync"): make it a check with the
  `correct` skill: a type, an analyzer, a banned API, a project reference, or a test whose
  name states the requirement.
- **A workaround**: fix the cause, or report it as open.
- **Commented-out code**: delete it.
- **A suppression's reason**: `[SuppressMessage(..., Justification = "...")]` on the
  narrowest member, never a `#pragma` with a comment.

Public API docs in `src/Kiji` are one-line `<summary>`s stating what an author can rely on;
add `<param>`, `<exception>` or `<remarks>` only when the contract needs them.
