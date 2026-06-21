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
- [ ] SQLite wiring + dual-monitor display output.

## P1 — MIDI playback (SoundFont)
- [ ] MIDI model + load (DryWetMIDI) in Core; KAR/XF aware.
- [ ] SoundFont synthesis (MeltySynth) + NAudio output behind a Core audio abstraction.
- [ ] Transport (play/pause/seek); **key change** (transpose) and **tempo change**.

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
