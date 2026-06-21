using Saikara.Core.Midi;

namespace Saikara.Core.Editor;

/// <summary>
/// Pure transform that applies <see cref="SongCorrections"/> to a <see cref="MidiSong"/>,
/// producing a new <see cref="CorrectedSong"/> with time-shifted lyric events and an
/// overridden melody track index. The original <see cref="MidiSong"/> is never mutated.
/// </summary>
/// <remarks>
/// Callers (e.g. the playback engine) apply this before building the telop or the scoring
/// reference:
/// <code>
/// var corrected = CorrectedSongBuilder.Apply(midiSong, corrections);
/// var telop = telopBuilder.Build(corrected.Lyrics);
/// int melodyTrack = corrected.MelodyTrackIndex ?? MelodyTrackDetector.Detect(midiSong) ?? 0;
/// </code>
/// </remarks>
public static class CorrectedSongBuilder
{
    /// <summary>
    /// Applies the given corrections to a <see cref="MidiSong"/> and returns a
    /// <see cref="CorrectedSong"/>. When <paramref name="corrections"/> is
    /// <see langword="null"/>, the song is returned unchanged (lyrics and melody index are
    /// taken as-is).
    /// </summary>
    /// <param name="song">The original parsed MIDI song.</param>
    /// <param name="corrections">
    /// The user's overrides, or <see langword="null"/> for no corrections.
    /// </param>
    /// <returns>A <see cref="CorrectedSong"/> with adjusted lyrics and melody track index.</returns>
    public static CorrectedSong Apply(MidiSong song, SongCorrections? corrections)
    {
        ArgumentNullException.ThrowIfNull(song);

        if (corrections is null)
        {
            return new CorrectedSong
            {
                Song = song,
                Lyrics = song.Lyrics,
                MelodyTrackIndex = null,
            };
        }

        // Build a lookup of per-syllable deltas keyed by lyric index for O(1) access.
        Dictionary<int, double>? perSyllableDeltas = null;
        if (corrections.PerSyllableAdjustments is { Count: > 0 } adjustments)
        {
            perSyllableDeltas = new Dictionary<int, double>(adjustments.Count);
            foreach (SyllableAdjustment adj in adjustments)
            {
                // Last-wins if duplicate indices exist.
                perSyllableDeltas[adj.LyricIndex] = adj.DeltaMs;
            }
        }

        double globalOffsetMs = corrections.LyricOffsetMs;
        bool hasGlobalOffset = globalOffsetMs != 0.0;
        bool hasPerSyllable = perSyllableDeltas is not null;

        IReadOnlyList<LyricEvent> lyrics;
        if (!hasGlobalOffset && !hasPerSyllable)
        {
            lyrics = song.Lyrics;
        }
        else
        {
            var adjusted = new List<LyricEvent>(song.Lyrics.Count);
            for (int i = 0; i < song.Lyrics.Count; i++)
            {
                LyricEvent original = song.Lyrics[i];
                double totalDeltaMs = globalOffsetMs;

                if (perSyllableDeltas is not null &&
                    perSyllableDeltas.TryGetValue(i, out double syllableDelta))
                {
                    totalDeltaMs += syllableDelta;
                }

                if (totalDeltaMs == 0.0)
                {
                    adjusted.Add(original);
                    continue;
                }

                // Shift the metric time by the total delta, clamping to >= 0.
                TimeSpan newTime = original.Time + TimeSpan.FromMilliseconds(totalDeltaMs);
                if (newTime < TimeSpan.Zero)
                    newTime = TimeSpan.Zero;

                adjusted.Add(original with { Time = newTime });
            }

            lyrics = adjusted;
        }

        return new CorrectedSong
        {
            Song = song,
            Lyrics = lyrics,
            MelodyTrackIndex = corrections.MelodyTrackIndex,
        };
    }
}
