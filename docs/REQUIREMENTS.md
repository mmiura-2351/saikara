# Saikara — Requirements

> A DAM-style karaoke application for Windows. Personal, non-commercial use only.

These requirements were agreed with the project owner during a structured interview.
They are the source of truth; the [roadmap](./ROADMAP.md) sequences them and the
[architecture](./ARCHITECTURE.md) describes how they are realized.

## 1. Goal & scope

- Build a karaoke app on the level of commercial systems (DAM) for **personal use**.
- **Non-commercial.** No commercial distribution is planned.
- Out of scope: ripping audio from streaming services (e.g. YouTube). See §7.

## 2. Audio engine

- **Source format: MIDI.** Songs are Standard MIDI Files (incl. `.kar` and Yamaha XF).
  - MIDI gives us, for free: a reference melody (for scoring & guide melody),
    key change (= transposition) and tempo change (= tempo-event rewrite).
- **Synthesis: SoundFont (SF2/SF3).** A good bundled SoundFont is rendered in software,
  far better sounding than the built-in Windows GS synth. Pure-managed synth first
  (MeltySynth), with FluidSynth as a later quality upgrade.

## 3. Core karaoke features

| Feature | Notes |
|---|---|
| Lyric telop | Two-line scrolling lyrics with per-character color wipe, synced to playback. |
| Full scoring | DAM-style: pitch accuracy, stability, dynamics, long-tone, vibrato, *shakuri*, *kobushi*. |
| Key / tempo change | Transpose semitones; adjust tempo. Trivial on MIDI. |
| Guide melody | Audible reference melody, toggleable. |

## 4. Content & lyrics

- **Both import and editing.** Import existing KAR/XF, *and* provide a correction
  **editor** to set the melody track and fix lyric timing.
- Rationale: full scoring needs an accurate reference melody and precise lyric timing;
  free KAR quality is uneven, so a correction layer is required.

## 5. Operation & UI

- **Two-window layout** (real-karaoke style):
  - **Operator window** — song-select remote (by number / title / artist search),
    reservation queue, key/tempo controls, score history.
  - **Display window** — lyric telop, background, real-time pitch bar, scoring result.
    Targets a secondary monitor (dual output).
- **Background**: static image / gradient first; extensible to looping video later.
- **Operation features**: reservation queue (multi-singer), song-select remote, score
  & history persistence.
  - (Recording playback was considered but de-scoped for now.)

## 6. Content acquisition (internet import)

- **MIDI/KAR import is the primary path** — import from local files and URLs / song-name
  search of MIDI/KAR sources into the library. Legally clean and aligned with the MIDI design.
- The library may also load **audio files the user legally owns** (secondary path).

## 7. Non-goals / constraints

- No streaming-service audio ripping (ToS / copyright concerns; also a poor fit for
  MIDI-based full scoring).
- No commercial use.

## 8. Platform

- **Windows**, native **WinUI 3** (Windows App SDK), .NET 8, C#, MVVM.
