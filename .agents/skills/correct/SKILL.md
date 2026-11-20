---
name: correct
description: Turn a correction or a repeated mistake into something that makes the mistake impossible or fail the build, instead of another line of prose. Use whenever a human corrects an agent's change, a review comment points at a pattern rather than a one-off, the same mistake appears a second time, or an AGENTS.md rule is broken. Also use when adding or changing a rule in AGENTS.md.
---

A rule that only lives in prose is read sometimes and followed sometimes. Code is the input
an agent copies most, so every workaround or deprecated pattern left in the tree is taught
again. Fix the cause where the agent cannot miss it.

## 1. Name the mistake class

Write one sentence: what was done, what should have been done, and where. Find other
occurrences in the tree and in history (`git log -S`, `git log --grep`, review comments,
reverted commits, workaround comments). One occurrence is a fix; a class exists once it
has happened twice or a human has corrected it.

## 2. Fix it at the highest level that works

Try each level in order and record why a higher one did not work.

1. **Codebase (make the wrong thing impossible).** One owner for each piece of state, one
   supported way to do each task, internals `internal` or `private` so the wrong call does
   not compile, one source of truth instead of lists kept in sync by hand. Delete the old
   way and its dead code in the same change.
2. **Types.** Make the bad state unrepresentable: nullable annotations, `required` and
   `init`, sealed hierarchies, a dedicated type instead of a string or a bool.
3. **Static checks.** When the bad code still compiles, make the build fail:
   - Raise an existing analyzer or IDE rule to `error` in `.editorconfig`.
     `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are already on.
   - Ban an API in a project's `BannedSymbols.txt` (`Microsoft.CodeAnalysis.BannedApiAnalyzers`,
     RS0030) with a message that names what to use instead; see `src/Kiji/BannedSymbols.txt`.
   - Write a syntax or semantic rule in `src/Kiji.Analyzers` (IDs `KR0001`...; record each in
     `AnalyzerReleases.Unshipped.md`). Every project already references it.
   - Check project structure (files, SDKs, references, packaging) in `Directory.Build.targets`
     with an `<Error Code="KR...">` whose text names the file or type to use.
   - Check repository-wide state (baselines, frozen files) in `src/Kiji.RepositoryTests`,
     which CI runs and `dotnet test -c Release` at the root includes.
   The error text must say how to fix the root cause, not only what is wrong: an agent does
   what the error tells it.
4. **Tests.** A test that fails when the behavior breaks. Fix or delete tests that pass while
   the behavior is broken, or fail when only internals change.
5. **Verification.** A journey in `.agents/skills/verify-site/features/` when the mistake is
   only visible by running the site.
6. **Prose, last.** AGENTS.md or a skill, only for what needs judgment and cannot be
   checked. Keep it to one sentence and add it to the rule table as `judgment`.

## 3. Block new occurrences first

When the pattern already exists in many places, make the check fail only on new
occurrences (an analyzer at `error` for new files, a CI step that compares against a counted
baseline, `[SuppressMessage]` on existing call sites), then clean up the old ones in
separate commits that lower the baseline. Never raise a baseline to make a change pass.

## 4. Prove the check

- Reproduce the real mistake (revert the fix, or reapply the bad change from history) and
  show the check failing with its message. Then show it passing on the fixed tree. A check
  that has never been seen failing is not evidence.
- Run the same command that CI runs (`dotnet build -c Release`, `dotnet test -c Release`).
- An exception to a check is a `[SuppressMessage(..., Justification = "...")]` or a
  `#pragma warning disable` on the narrowest scope, with a justification that names the
  external constraint, and only with a human's approval in the pull request. No
  file-wide or project-wide suppressions.

## 5. Update the rule table

Every rule in AGENTS.md's rule table names what enforces it. In the same change:

- Add or update the row for the rule you encoded, naming the check.
- Delete the prose rule once its check covers it; keep only the table row.
- If a rule marked `judgment` recurs, it is no longer judgment: encode it now.
- Delete a rule whose mistake can no longer happen.

## Report

For each mistake class: the evidence (commits or comments), the level chosen and why higher
levels did not work, the check's failing output on the real mistake, and the table change.
One commit per class.
