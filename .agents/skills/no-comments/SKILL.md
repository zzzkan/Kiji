---
name: no-comments
description: Remove code comments that explain our own code, and fix the code they excuse. Use when writing or reviewing code with comments, when a diff adds a comment, or when cleaning up a file's existing comments. Covers C#, MSBuild, YAML, PowerShell, shell and Razor comments.
---

Comments go stale, people skip them, nothing enforces them, and they are where an agent
justifies a workaround instead of fixing it. The code itself, its names, types, tests and
checks, carries the meaning. A comment that explains our own code is a sign that the code
should change.

## What may stay

Only these:

1. XML documentation comments (`///`) on public API. They become the package's IntelliSense
   and the API reference. Describe behavior and contracts, not implementation.
2. License and notice headers.
3. Behavior forced on us by something we cannot change (the .NET runtime or SDK, MSBuild,
   ASP.NET Core, Blazor, a NuGet package, the OS, a browser, GitHub Actions), stated as that
   external fact, ideally with an issue or documentation link.
4. Justifications required by tools, such as `[SuppressMessage(Justification = ...)]`.
5. Usage lines at the top of a script, stating its arguments.

When it is unclear whether a comment fits one of these, delete it.

## Everything else

For each comment in scope:

- **Explains what the code does**: delete it. If the code is unclear without it, rename,
  extract a method or introduce a type until it is clear.
- **Explains why our code is shaped this way**: make the code say it (a name, a type, a test
  whose name states the requirement), or record the decision in AGENTS.md if it is a
  judgment future changes must respect. Then delete the comment.
- **Constraint ("do not remove", "must run before X", "keep in sync with Y")**: encode it
  with the correct skill (a type, an ordering the API forces, a test, a check), then delete
  the comment. If it cannot be encoded now, delete the comment and report the constraint as
  open.
- **Workaround, TODO, "temporary", "for now"**: fix the root cause in scope, or delete the
  workaround with its comment. If the root cause is out of scope, report it as open.
- **Commented-out code**: delete it; git keeps history.
- **Suppressions** (`#pragma warning disable`, `[SuppressMessage]`, `<NoWarn>`): remove them and
  fix the code when the rule protects correctness. Keep them only on the narrowest scope with
  a justification that names an external constraint.

Words like "IMPORTANT", "do not remove" or "too risky" prompt a check, not a decision: trace
the symbol and run the tests before keeping or removing what they guard.

## Scope

- New and changed lines: always. A change adds no comments outside the list above.
- Existing comments: clean them in the files a change already touches, or in a dedicated
  cleanup change per area.
- Do not change behavior while removing comments, except to fix a workaround the comment
  excused. Run `dotnet build -c Release` and `dotnet test -c Release` after each file.

## The check

`CommentBaselineTests` in `src/Kiji.RepositoryTests` reads every C# file that git tracks or
would track. Consecutive `//` lines form one block; `/* */` is one block; `///` is not
counted; a block that contains a URL is allowed (an external constraint citing its source).
Every other block must be listed in `src/Kiji.RepositoryTests/comment-baseline.txt` by a
hash of its text and its path. So:

- A new or reworded comment fails with its file and line. Delete it.
- A deleted comment fails until its line is removed from the baseline. Remove the line the
  failure names.
- Moving a file or class drops its allowance: the moved comments are new. Delete them.
- Never add lines to the baseline.

```sh
cd src/Kiji.RepositoryTests && dotnet test -c Release
```

## Report

Files touched, comments deleted, comments kept with the rule that keeps each, code changes
made instead of comments, constraints encoded, and constraints left open.
