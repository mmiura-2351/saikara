# Saikara （最カラ）

A DAM-style karaoke application for Windows. **Personal, non-commercial project.**

MIDI + SoundFont engine, synced lyric telop, DAM-style full scoring (pitch, stability,
dynamics, vibrato, *shakuri*, *kobushi*…), key/tempo change, guide melody, a two-window
operator/display layout, a reservation queue, and library/score history.

## Status

Early development. See [docs/ROADMAP.md](docs/ROADMAP.md) for the plan and progress.

## Run it (no build needed)

GitHub Actions builds a ready-to-run, self-contained Windows package on every push to
`main` — no Visual Studio, .NET, or Windows App SDK install required.

1. Open the [latest **CI** run on `main`](https://github.com/mmiura-2351/saikara/actions/workflows/ci.yml?query=branch%3Amain)
   (or trigger one via **Run workflow**).
2. Download the **`saikara-win-x64`** artifact and unzip it.
3. Run **`Saikara.App.exe`**. (Unsigned, so Windows SmartScreen may warn — *More info →
   Run anyway*. Code signing comes in the packaging phase.)
4. First launch downloads the default SoundFont (~32 MB) to
   `%LOCALAPPDATA%\Saikara\soundfonts\`. Then **Open MIDI file…** → **Play**.

## Tech

WinUI 3 · .NET 8 · C# · MVVM · DryWetMIDI · MeltySynth/FluidSynth · NAudio (WASAPI) · SQLite.

Logic lives in the platform-agnostic `Saikara.Core` (unit-tested on any OS); the WinUI UI
lives in `Saikara.App` (Windows). See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Build & test

```bash
# Cross-platform core (any OS):
dotnet test tests/Saikara.Core.Tests/Saikara.Core.Tests.csproj

# Full app (Windows only):
dotnet build saikara.sln -c Release
```

## Documentation

- [Requirements](docs/REQUIREMENTS.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
- [Contributor/agent guide (CLAUDE.md)](CLAUDE.md)
