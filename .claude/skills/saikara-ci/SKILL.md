---
name: saikara-ci
description: Ship a Saikara change via branch -> PR -> CI -> merge, and triage CI failures (especially Windows-only WinUI build errors that cannot be reproduced on the Linux dev box).
---

# Ship & verify via CI

1. **Branch** off `main`; commit with an English message + the required trailers. Push.
2. **PR:** `gh pr create --fill`. CI runs two jobs — Linux Core tests and the Windows
   solution build.
3. **Watch:** `gh pr checks` or `gh run watch <id> --exit-status --interval 15`.
4. **Triage failures:** `gh run view <id> --log-failed`.
   - The Windows job is the **only** place WinUI compiles, so XAML errors, WindowsAppSDK
     version mismatches, and missing usings surface there, not locally. Read the log, fix,
     push again.
   - Core failures reproduce locally: `dotnet test tests/Saikara.Core.Tests/...`.
5. **Merge** when green: `gh pr merge --squash --delete-branch`. Keep `main` green
   (Core tests on Linux + solution build on Windows).
