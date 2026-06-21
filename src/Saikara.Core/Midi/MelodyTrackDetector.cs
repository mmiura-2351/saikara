namespace Saikara.Core.Midi;

/// <summary>
/// Best-effort detection of the reference-melody track in a <see cref="MidiSong"/>, used
/// to seed the guide melody and scoring.
/// </summary>
/// <remarks>
/// This is a <b>heuristic</b>, not a guarantee. KAR/XF files are inconsistent, so detection
/// currently only trusts an explicit track name containing "melody" (case-insensitive) and
/// returns <see langword="null"/> otherwise. The P6 correction editor lets the user override
/// the result, which is the authoritative source of the melody track. Keep this conservative:
/// a wrong automatic pick is worse than none, because it silently corrupts scoring.
/// </remarks>
public static class MelodyTrackDetector
{
    private const string MelodyKeyword = "melody";

    /// <summary>
    /// Returns the zero-based index of the first track whose <see cref="MidiTrack.Name"/>
    /// contains "melody" (case-insensitive) and that has at least one note, or
    /// <see langword="null"/> when no such track is found. Best-effort only — see the type
    /// remarks.
    /// </summary>
    /// <param name="song">The song to inspect.</param>
    /// <returns>The detected melody track index, or <see langword="null"/>.</returns>
    public static int? Detect(MidiSong song)
    {
        ArgumentNullException.ThrowIfNull(song);

        for (int i = 0; i < song.Tracks.Count; i++)
        {
            MidiTrack track = song.Tracks[i];
            if (track.Notes.Count == 0)
            {
                continue;
            }

            if (track.Name is { } name &&
                name.Contains(MelodyKeyword, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return null;
    }
}
