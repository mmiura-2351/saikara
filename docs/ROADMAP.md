# Saikara — Roadmap

Phased plan. Each phase should leave `main` green (Core tests + Windows build).
Logic lands in `Saikara.Core` with tests; UI lands in `Saikara.App`.
Tracked in detail as GitHub issues/milestones.

## P0 — Foundation
- [x] Solution, `Saikara.Core` + tests, building/testing on Linux.
- [x] CI: Core tests on Linux, full solution build on Windows.
- [x] Docs (requirements / architecture / roadmap) + CLAUDE.md.
- [x] `Saikara.App` WinUI 3 skeleton: operator window + display window, MVVM, DI host.
      (Built green on Windows CI; reviewed with `winui:winui-code-review`. PR #11.)
- [x] SQLite-backed song library in Core (`ISongLibrary` / `SqliteSongLibrary`, 15 tests). PR #12.
- [x] App wiring: DI-register `ISongLibrary` + init at startup; operator search uses it;
      dual-monitor display placement. (PR #13; green on Windows CI.)

**P0 complete.** ✅ Foundation, Core (`MusicMath` + SQLite song library, 30 tests),
two-window WinUI app, CI (Linux + Windows), and the autonomous-dev environment are all in place.

## P1 — MIDI playback (SoundFont)
- [x] MIDI model + load (DryWetMIDI) in Core; KAR/XF aware. Plus key/tempo transforms
      (`MidiTransforms`) and best-effort melody detection. 31 tests. PR #14.
- [ ] SoundFont synthesis (MeltySynth) + NAudio output behind a Core audio abstraction.
      (SoundFont asset: user-specified `.sf2` + optional first-run download of a free
      default; large binaries are git-ignored, not committed.)
      - [x] Core abstraction + assets: `IAudioEngine` (transport + key/tempo) interface,
            `SoundFontInstaller` (atomic first-run download), `MidiSerializer` (re-emit a
            transformed `MidiSong` as SMF for the synth). 23 tests. (Linux-testable part.)
      - [ ] App: `MeltySynthAudioEngine : IAudioEngine` (MeltySynth + NAudio/WASAPI),
            driven from the serialized transformed song. (Windows.)
- [ ] Transport (play/pause/seek) wiring; expose `MidiTransforms` key/tempo in the operator UI.

## P2 — Lyric telop
- [ ] Parse KAR/XF lyric + timing events in Core.
- [ ] Two-line color-wipe telop synced to playback (Win2D/SkiaSharp).
- [ ] Guide-melody on/off.

## P3 — Mic & pitch
- [ ] WASAPI mic capture (App) feeding a Core `PitchDetector` (MPM/YIN).
- [ ] Real-time pitch bar vs reference melody on the display window.
- [ ] Latency calibration between synthesized backing and mic.

## P4 — Scoring (basic -> full)
- [ ] Pitch-accuracy score in Core (deterministic, unit-tested).
- [ ] Full DAM-style metrics: stability, dynamics, long-tone, vibrato, *shakuri*, *kobushi*.
- [ ] Result screen.

## P5 — Library & operation
- [ ] Song-select remote (by number / title / artist search).
- [ ] Reservation queue (multi-singer).
- [ ] Score & history persistence (SQLite).

## P6 — Correction editor
- [ ] Designate melody track; correct lyric timing; save back to library.

## P7 — Internet import
- [ ] Import MIDI/KAR from local files and URLs into the library.
- [ ] (Optional) song-name search of MIDI/KAR sources.

## P8 — Polish
- [ ] Background (static/gradient -> looping video).
- [ ] UI refinement, settings, stabilization.
