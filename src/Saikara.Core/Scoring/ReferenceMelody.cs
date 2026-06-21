using Saikara.Core.Midi;
using Saikara.Core.Music;

namespace Saikara.Core.Scoring;

/// <summary>
/// Builds the <see cref="ReferenceNote"/> sequence the scoring engine compares the singer against,
/// from the designated melody track of a <see cref="MidiSong"/>. This is the bridge between the
/// MIDI model (P1) and the scoring engine (P4).
/// </summary>
public static class ReferenceMelody
{
    /// <summary>
    /// Extracts the time-ordered reference melody from one track of a song, transposing every
    /// note by <paramref name="semitoneOffset"/> so the reference matches the key the singer hears.
    /// </summary>
    /// <param name="song">The parsed song.</param>
    /// <param name="trackIndex">
    /// Zero-based index of the melody track (e.g. from <see cref="MelodyTrackDetector"/> or the
    /// P6 correction editor). Must be a valid track index.
    /// </param>
    /// <param name="semitoneOffset">
    /// The current key change in semitones; pass the same value used to transpose playback so the
    /// reference and the backing track agree. Each note is shifted via
    /// <see cref="MusicMath.Transpose(int, int)"/> (clamped to 0-127). Pass <c>0</c> for no change.
    /// </param>
    /// <returns>
    /// The track's notes as <see cref="ReferenceNote"/> values, ordered by start time. Empty when
    /// the track has no notes.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="song"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="trackIndex"/> is out of range.</exception>
    public static IReadOnlyList<ReferenceNote> FromTrack(MidiSong song, int trackIndex, int semitoneOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (trackIndex < 0 || trackIndex >= song.Tracks.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trackIndex), trackIndex, "Track index is outside the song's tracks.");
        }

        MidiTrack track = song.Tracks[trackIndex];

        return track.Notes
            .OrderBy(n => n.StartTime)
            .Select(n => new ReferenceNote
            {
                Start = n.StartTime,
                End = n.EndTime,
                MidiNote = MusicMath.Transpose(n.NoteNumber, semitoneOffset),
            })
            .ToList();
    }
}
