# Saikara — Architecture

## Tech stack

| Concern | Choice |
|---|---|
| UI | WinUI 3 (Windows App SDK), .NET 8, C#, MVVM (CommunityToolkit.Mvvm) |
| MIDI parse/manipulate | DryWetMIDI (read KAR/XF, transpose = key change, tempo rewrite) |
| SoundFont synthesis | MeltySynth (pure C#) first; FluidSynth (SF3, effects) as upgrade |
| Audio I/O | NAudio over WASAPI (low-latency playback + mic capture) |
| Pitch detection | McLeod Pitch Method (MPM) / YIN, implemented in `Saikara.Core` |
| Telop / visuals | Win2D or SkiaSharp (precise per-character color wipe) |
| Storage | SQLite (library, score history, reservation queue) |

## The Core / App split (key decision)

The dev/CI environment runs **Linux**, where **WinUI 3 cannot be built** (Windows-only).
To keep most logic verifiable without Windows, code is split so that the hard,
algorithmic parts are platform-agnostic and unit-tested anywhere:

```
saikara.sln
├─ src/
│  ├─ Saikara.Core/      net8.0           PLATFORM-AGNOSTIC. Builds & tests on Linux.
│  │                                      MIDI model, key/tempo transforms, pitch
│  │                                      detection, scoring, lyric timing, library logic.
│  └─ Saikara.App/       net8.0-windows…  WinUI 3 UI ONLY. Windows / CI only.
│                                         Windows, view-models, telop rendering, audio I/O.
└─ tests/
   └─ Saikara.Core.Tests/ net8.0          xUnit. Runs on Linux & CI.
```

Rule of thumb: **if it can be expressed without a UI or Windows-only API, it belongs in
`Saikara.Core`** so it can be tested on Linux. `Saikara.App` stays a thin, mostly-declarative
shell that binds view-models to `Core` services.

## Layers (target design)

- **Domain** (`Core`): `Song`, `MidiData`, `MelodyTrack`, `LyricLine`, `ScoreResult`,
  `ReservationQueue`.
- **Services** (`Core`): `MidiImportService` (KAR/XF parse, melody-track detection),
  `PlaybackEngine` (synth + transpose/tempo), `PitchDetector`, `ScoringEngine`,
  `LyricsSyncService`, `LibraryRepository` (SQLite), `LyricEditorService`, `NetImportService`.
  - Audio I/O and synthesis have a `Core` abstraction (interface) with the concrete
    NAudio/MeltySynth implementation living in `App` (Windows).
- **UI** (`App`): operator window + display window, view-models, telop/visualizer rendering.

## Verification strategy

- **Linux (local + `ci` job):** `dotnet test` on `Saikara.Core.Tests`. This is the fast,
  always-on feedback loop and must stay green.
- **Windows (`windows` CI job, `windows-latest`):** builds the full solution including
  `Saikara.App`. This is the only place the WinUI app is compiled; it is the autonomous
  build-verification backstop. UI behavior testing uses the WinUI UI-testing tooling later.

See [CLAUDE.md](../CLAUDE.md) for the day-to-day workflow.
