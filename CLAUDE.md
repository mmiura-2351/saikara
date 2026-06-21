# CLAUDE.md — Saikara

DAM-style karaoke app for Windows (WinUI 3, .NET 8). Personal, non-commercial.
Read [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md),
and [docs/ROADMAP.md](docs/ROADMAP.md) before substantial work.

## The one constraint that shapes everything

**The dev/CI box is Linux. WinUI 3 (`Saikara.App`) cannot be built here — it is Windows-only.**
Therefore:

- Put all logic (MIDI, key/tempo transforms, pitch detection, scoring, lyric timing,
  library) in **`Saikara.Core`** (`net8.0`, platform-agnostic) so it builds and is
  unit-tested locally on Linux.
- Keep **`Saikara.App`** (`net8.0-windows…`, WinUI) a thin UI shell. It is compiled only
  on the **Windows CI job**.
- Audio I/O / synthesis are abstracted by interfaces in `Core`; the NAudio/MeltySynth
  implementations live in `App`.

When you write a feature, ask: *can this be expressed without UI / Windows APIs?* If yes,
it goes in `Core` with tests.

## Commands

```bash
# Fast local loop (Linux) — MUST stay green:
dotnet test tests/Saikara.Core.Tests/Saikara.Core.Tests.csproj --nologo

# Build only the cross-platform projects locally:
dotnet build src/Saikara.Core/Saikara.Core.csproj
# NOTE: `dotnet build saikara.sln` FAILS on Linux once Saikara.App exists (Windows target).
#       The full solution is built on the Windows CI job, not locally.

# Check CI after pushing:
gh run list --branch <branch> --limit 5
gh run watch        # or: gh run view --log-failed
```

SDK is pinned to .NET 8 via `global.json` (the box also has a .NET 10 SDK; do not target it).

## Workflow (autonomous)

- The owner monitors via GitHub; keep work visible. Track phases as issues/milestones
  (`gh issue ...`), update [docs/ROADMAP.md](docs/ROADMAP.md) checkboxes as phases land.
- Branch per unit of work off `main`; open a PR; let CI verify; merge when green.
  Never push directly to `main` for non-trivial changes.
- `main` must stay green: **Core tests (Linux) + full build (Windows)**.
- Commit/PR text and all code/comments/docs are in **English**. Chat with the owner is in
  Japanese.
- Don't introduce streaming-rip features or anything commercial (see requirements §7).

## Conventions

- MVVM with CommunityToolkit.Mvvm; prefer `x:Bind`. WinUI specifics: see the `winui:*`
  skills (winui-design, winui-dev-workflow, winui-code-review, winui-ui-testing).
- New algorithmic code in `Core` is TDD-first: write the xUnit test, make it pass on Linux.
- Add NuGet packages only when first used; pin versions; prefer stable releases.

## Sustaining autonomous development

The owner monitors only; this project must self-sustain across a long, context-limited run.

- **Delegate** self-contained work to subagents — the `saikara-core-dev` agent for Core
  features, `winui:winui-dev` for UI, `Explore` for searches — so heavy work happens in
  their context, not the main one. Keep the main context lean; relay conclusions.
- **Skills** encode the recurring loops: `saikara-feature` (build a feature),
  `saikara-ci` (branch→PR→CI→merge & triage), `saikara-maintenance` (periodic upkeep).
- **External memory** survives context compaction: keep the harness memory files and these
  in-repo docs current. Surface progress on GitHub (issues, PRs, green CI).
- **Maintenance:** run `saikara-maintenance` at phase boundaries — keep ROADMAP, issues,
  CLAUDE.md, memory, skills, and agents from drifting away from the code.

## Status

P0 in progress. Done: solution, `Saikara.Core` (`MusicMath`) + 15 passing tests, CI green
(Linux + Windows), docs, and the autonomous-dev infra (agent, skills, memory).
Next: scaffold `Saikara.App` (WinUI skeleton) and get the Windows build green. See roadmap.
