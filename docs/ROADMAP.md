# Saikara — Roadmap

Phased plan. Each phase should leave `main` green (Core tests + Windows build).
Logic lands in `Saikara.Core` with tests; UI lands in `Saikara.App`.
Tracked in detail as GitHub issues/milestones.

## P0 — Foundation ✅
- [x] Solution, `Saikara.Core` + tests, building/testing on Linux.
- [x] CI: Core tests on Linux, full solution build on Windows.
- [x] Docs (requirements / architecture / roadmap) + CLAUDE.md.
- [x] `Saikara.App` WinUI 3 skeleton (PR #11).
- [x] SQLite-backed song library (PR #12). App wiring (PR #13).

## P1 — MIDI playback (SoundFont) ✅ (runtime-verified)
- [x] MIDI model + load (DryWetMIDI); key/tempo transforms; melody detection (PR #14).
- [x] Core audio abstraction + SoundFont installer + MIDI serializer (PR #15).
- [x] App `MeltySynthAudioEngine` (MeltySynth + NAudio/WASAPI) + transport UI (PR #16).
- [x] Bug fixes: synth reset + device pause during rebuild (PRs #18–#19).

## P2 — Lyric telop ✅
- [x] Core lyric model: `LyricTelopBuilder`/`TelopPlayback` (PR #20).
- [x] Two-line color-wipe telop on display window (gradient wipe, 30 fps) (PR #21).
- [x] Guide-melody on/off (Core `MuteTrack`/`UnmuteTrack` + App toggle) (PR #30).

## P3 — Mic & pitch ✅ (runtime-verify mic path)
- [x] Core McLeod pitch detector (MPM) (PR #22).
- [x] App WASAPI mic capture (`PitchMonitor`) + live pitch bar vs reference melody (PR #24).
- [ ] Latency calibration (currently fixed 100 ms offset; auto/UI calibration TODO).

## P4 — Scoring (basic -> full) ✅ (runtime-verify scored results)
- [x] Core DAM-style `ScoringEngine` — pitch accuracy, stability, expression, long-tone,
      vibrato/shakuri/kobushi, overall + grade (PR #23).
- [x] Mic -> `PitchSample` accumulation (PR #24). Result screen overlay (PR #25).

## P5 — Library & operation ✅ (runtime-verify)
- [x] Song-select remote (by number/title/artist search) — built into operator since P0.
- [x] Reservation queue UI — operator, since P0 (multi-singer queue management TODO).
- [x] Score & history persistence: Core `SqliteScoreHistory` (PR #26) + App save-on-result +
      best/recent display (PR #29).
- [x] Library import (file + URL) + play-from-library + `CurrentSong` tracking (PR #28).

## P6 — Correction editor ✅
- [x] Core: `SongCorrections`/`CorrectedSongBuilder`/`SqliteSongCorrectionsStore` (PR #31).
- [x] App: melody track ComboBox + lyric offset NumberBox + Save/Reset; engine applies
      corrections before telop/reference/mute (PR #32).

## P7 — Internet import ✅
- [x] Core `MidiImportService` — local file + URL import, validate, atomic copy, KAR metadata
      extraction, content-addressed Number, upsert to library (PR #27).
- [x] App import UI (FileOpenPicker + URL ContentDialog) + play-from-library (PR #28).
- [ ] (Optional/future) song-name search of MIDI/KAR sources.

## P8 — Polish ✅ (core features done)
- [x] Reservation queue auto-advance (3s delay, auto-load+play next song) (PR #32).
- [x] Settings: latency offset NumberBox + SoundFont path display (PR #32).
- [ ] (Future) Background looping video; further UI refinement.

---

> **All planned phases (P0–P8) are essentially complete.** 253 Core tests, 32 merged PRs,
> main green. Future enhancements: background video, song-name search, advanced per-syllable
> lyric editor, UI polish.
