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

- **Always use the official Microsoft `winui:*` skills for WinUI work** — don't hand-roll
  WinUI knowledge. Map them to the stage:
  - design / new XAML / theming → `winui:winui-design`
  - build & run / build-error diagnosis → `winui:winui-dev-workflow` (Windows only)
  - quality review before merge → `winui:winui-code-review`
  - automated UI tests → `winui:winui-ui-testing`
  - MSIX / signing / release → `winui:winui-packaging`
  - WPF porting → `winui:winui-wpf-migration`
  - Delegate larger WinUI builds to the `winui:winui-dev` agent.
- MVVM with CommunityToolkit.Mvvm; prefer `x:Bind` with an explicit `Mode`. On `net8.0`
  (C# 12) use the `[ObservableProperty]` **field** syntax (partial-property syntax needs C# 13).
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

P0 nearly complete. Done: solution; `Saikara.Core` (`MusicMath`) + 15 passing tests;
CI green (Linux + Windows); docs; autonomous-dev infra (agent, skills, memory); and the
`Saikara.App` WinUI skeleton (two-window MVVM + DI host) building green on Windows CI (PR #11).
Next in P0: SQLite wiring + dual-monitor display placement. Then P1 (MIDI playback). See roadmap.
