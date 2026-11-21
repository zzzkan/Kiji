---
name: correct
description: Use when addressing a review correction, a repeated mistake or a change to repository rules.
---

1. Reproduce the mistake and check for other occurrences.
2. Fix it at the source. Prefer a single owner for data, a type that rules out an
   invalid value, or a project boundary that prevents an invalid dependency.
3. When the mistake still compiles, use the smallest check that catches it. Reuse
   existing compiler diagnostics, analyzers or behavior tests. Put guidance that
   requires judgment in the relevant skill or AGENTS.md and remove guidance the
   change has made redundant.
   If existing violations need a baseline, lower it as they are fixed. Keep suppressions
   narrow and document the external constraint; get human approval in the pull request.
4. Verify that the original mistake fails and the corrected case passes. Report the
   reproducer and result. For user-visible behavior, drive the affected site feature.
