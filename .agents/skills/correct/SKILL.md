---
name: correct
description: Use when addressing a review correction, a repeated mistake or a change to repository rules.
---

Fix the mistake where it originates, then make a recurrence fail the build or a test instead
of adding prose. Prefer, in order: a single owner for the data, a type that rules out the
invalid value, a project boundary, an existing diagnostic or banned API
(`BannedSymbols*.txt`), an MSBuild check in `Directory.Build.targets`, an analyzer in
`tools/Kiji.Analyzers`, a test in `tests/Kiji.RepositoryTests`. A failure message says what
to do instead.

A new KR check needs a test that makes it fail (`Kiji.Analyzers.Tests`, or
`ProjectBoundaryTests` for MSBuild checks). Suppressions stay as narrow as one member, give
their reason in the suppression, and need the reviewer's approval in the pull request.

Only a decision that needs judgment and cannot be checked goes into AGENTS.md or a skill;
remove prose a new check makes redundant.
