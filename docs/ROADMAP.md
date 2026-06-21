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
- [x] SoundFont synthesis (MeltySynth) + NAudio output behind a Core audio abstraction.
      (SoundFont asset: first-run auto-download of a free default; git-ignored, not committed.)
      - [x] Core abstraction + assets: `IAudioEngine` (transport + key/tempo) interface,
            `SoundFontInstaller` (atomic first-run download), `MidiSerializer` (re-emit a
            transformed `MidiSong` as SMF for the synth). 23 tests. (Linux-testable part.)
      - [x] App: `MeltySynthAudioEngine : IAudioEngine` (MeltySynth 2.4.1 + NAudio 2.2.1/WASAPI),
            driven from the serialized transformed song. PR #16 (build-green on Windows CI).
- [x] Transport wiring + key/tempo in the operator UI: Open MIDI file, play/pause/stop,
      seek slider, key/tempo bound to the engine. PR #16.

> **P1 complete and runtime-verified by the owner.** Audio plays via SoundFont; key/tempo/seek
> all work. A momentary swap glitch was fixed (synth reset + device pause during rebuild, PRs #18–#19).

## P2 — Lyric telop
- [x] Parse KAR/XF lyric + timing events in Core (`MidiSong.Lyrics`; `LyricTelopBuilder`
      groups into `TelopLine`/`TelopSyllable`; `TelopPlayback` wipe helper). PR #20.
- [x] Two-line color-wipe telop synced to playback (native gradient wipe; `ITelopSource`
      + `DisplayViewModel` 30 fps). PR #21. (Needs owner runtime visual check.)
- [ ] Guide-melody on/off. (App: play the melody track audibly; toggle.)

## P3 — Mic & pitch
- [x] Core `PitchDetector` (McLeod/MPM): `IPitchDetector`/`McLeodPitchDetector`,
      `PitchResult`. 25 tests. PR #22.
- [x] WASAPI mic capture (App) feeding the Core `PitchDetector` (`PitchMonitor`). PR #24.
- [x] Real-time pitch bar vs reference melody on the display window (Canvas bar). PR #24.
- [ ] Latency calibration between synthesized backing and mic (currently a fixed default
      offset; auto/UI calibration still TODO).

## P4 — Scoring (basic -> full)
- [x] Pitch-accuracy score in Core (deterministic, unit-tested).
- [x] Full DAM-style metrics in Core: stability, expression (dynamics), long-tone, vibrato,
      *shakuri*, *kobushi* (`Saikara.Core.Scoring`: `ScoringEngine`/`IScoringEngine`,
      `ScoreResult`, `ScoringOptions`, `PitchSample`, `ReferenceNote`, `ReferenceMelody`).
      34 tests.
- [x] Mic -> `PitchSample` plumbing in App (`PitchMonitor`: latency-corrected playback time +
      RMS energy, accumulated for scoring). PR #24.
- [x] Result screen: score on song end, overlay with Overall + Grade + sub-scores + technique
      counts. PR #25. (Needs owner runtime check — real score depends on mic + latency.)

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
