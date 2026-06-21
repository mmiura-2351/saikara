using Saikara.Core.Music;

namespace Saikara.Core.Midi;

/// <summary>
/// Pure functions that produce a transformed copy of a <see cref="MidiSong"/> for the
/// key-change and tempo-change karaoke features. They operate on the model (not the raw
/// SMF), are deterministic, and do not mutate their input — so the audio layer can swap
/// the active song without re-reading the file.
/// </summary>
public static class MidiTransforms
{
    /// <summary>The percussion channel (zero-based index 9 == MIDI channel 10).</summary>
    private const int PercussionChannel = 9;

    /// <summary>
    /// Transposes every melodic note by <paramref name="semitones"/>, returning a new
    /// <see cref="MidiSong"/>. Note numbers are clamped to the valid 0-127 range via
    /// <see cref="MusicMath.Transpose(int, int)"/>. Notes on the percussion channel
    /// (zero-based index 9 / MIDI channel 10) are left unchanged, because there each note
    /// number selects a drum sound, not a pitch. Timing, tempo and lyrics are unchanged.
    /// </summary>
    /// <param name="song">The song to transpose. Not mutated.</param>
    /// <param name="semitones">Signed number of semitones to shift (0 returns an equivalent copy).</param>
    /// <returns>A transposed copy of <paramref name="song"/>.</returns>
    public static MidiSong Transpose(MidiSong song, int semitones)
    {
        ArgumentNullException.ThrowIfNull(song);

        if (semitones == 0)
        {
            return song;
        }

        var tracks = song.Tracks
            .Select(track => track with
            {
                Notes = track.Notes
                    .Select(note => note.Channel == PercussionChannel
                        ? note
                        : note with { NoteNumber = MusicMath.Transpose(note.NoteNumber, semitones) })
                    .ToList(),
            })
            .ToList();

        return song with { Tracks = tracks };
    }

    /// <summary>
    /// Scales the song's tempo by a percentage, returning a new <see cref="MidiSong"/>.
    /// 100 means the original tempo; 150 plays at 1.5x speed (faster, shorter); 50 at half
    /// speed. Every tempo change and all metric times (note start/length, lyric times,
    /// total duration) are scaled; tick positions are unchanged. The musical pitch is not
    /// affected — only playback speed.
    /// </summary>
    /// <param name="song">The song to retempo. Not mutated.</param>
    /// <param name="percent">Tempo scale as a percentage; must be positive.</param>
    /// <returns>A retempo-ed copy of <paramref name="song"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="percent"/> is not positive.</exception>
    public static MidiSong ScaleTempo(MidiSong song, double percent)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (percent <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "Tempo percent must be positive.");
        }

        // A faster tempo (higher BPM) means fewer microseconds per quarter note, so the
        // micros-per-quarter scale is the inverse of the speed scale.
        double speedScale = percent / 100.0;
        double microsScale = 1.0 / speedScale;
        return ApplyTempoScale(song, microsScale);
    }

    /// <summary>
    /// Sets the song's initial tempo to a target BPM and scales the rest of the tempo map
    /// (and all metric times) by the same ratio, returning a new <see cref="MidiSong"/>.
    /// The shape of any tempo map is preserved — every change is rescaled relative to the
    /// initial tempo — so songs with tempo automation still feel right, just at the new
    /// base tempo. Pitch is unaffected.
    /// </summary>
    /// <param name="song">The song to retempo. Not mutated.</param>
    /// <param name="targetBeatsPerMinute">The desired initial tempo in BPM; must be positive.</param>
    /// <returns>A retempo-ed copy of <paramref name="song"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="targetBeatsPerMinute"/> is not positive.</exception>
    public static MidiSong SetBeatsPerMinute(MidiSong song, double targetBeatsPerMinute)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (targetBeatsPerMinute <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetBeatsPerMinute), targetBeatsPerMinute, "Target BPM must be positive.");
        }

        // microsScale maps original micros-per-quarter to the new value for the initial tempo,
        // and is applied uniformly to keep the tempo map's proportions.
        long currentMicros = song.InitialTempo.MicrosecondsPerQuarterNote;
        long targetMicros = (long)Math.Round(60_000_000.0 / targetBeatsPerMinute);
        double microsScale = (double)targetMicros / currentMicros;
        return ApplyTempoScale(song, microsScale);
    }

    private static MidiSong ApplyTempoScale(MidiSong song, double microsScale)
    {
        // Metric time scales linearly with microseconds-per-quarter when tick positions
        // are held constant, so all metric fields scale by the same factor.
        double timeScale = microsScale;

        var tempoChanges = song.TempoChanges
            .Select(tc => tc with
            {
                Time = Scale(tc.Time, timeScale),
                MicrosecondsPerQuarterNote = ScaleMicros(tc.MicrosecondsPerQuarterNote, microsScale),
            })
            .ToList();

        var tracks = song.Tracks
            .Select(track => track with
            {
                Notes = track.Notes
                    .Select(note => note with
                    {
                        StartTime = Scale(note.StartTime, timeScale),
                        Length = Scale(note.Length, timeScale),
                    })
                    .ToList(),
            })
            .ToList();

        var lyrics = song.Lyrics
            .Select(l => l with { Time = Scale(l.Time, timeScale) })
            .ToList();

        return song with
        {
            Duration = Scale(song.Duration, timeScale),
            TempoChanges = tempoChanges,
            Tracks = tracks,
            Lyrics = lyrics,
        };
    }

    private static TimeSpan Scale(TimeSpan value, double scale)
        => TimeSpan.FromTicks((long)Math.Round(value.Ticks * scale));

    private static long ScaleMicros(long micros, double scale)
        => Math.Clamp((long)Math.Round(micros * scale), 1, 16_777_215);
}
