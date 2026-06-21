namespace Saikara.Core.Midi;

/// <summary>
/// A single track of a <see cref="MidiSong"/> (one SMF track chunk). Carries its optional
/// name, the set of MIDI channels its notes use, and its notes. Melody-track detection and
/// the guide melody operate on tracks; scoring reads notes from the designated melody track.
/// </summary>
public sealed record MidiTrack
{
    /// <summary>
    /// Track name from the <c>Sequence/Track Name</c> meta event, or <see langword="null"/>
    /// if the track has no name. Used by best-effort melody detection.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Distinct zero-based MIDI channels (0-15) used by this track's notes, in ascending
    /// order. Empty when the track has no notes (e.g. a conductor/tempo track).
    /// </summary>
    public required IReadOnlyList<int> Channels { get; init; }

    /// <summary>The track's notes, ordered by start time.</summary>
    public required IReadOnlyList<MidiNote> Notes { get; init; }
}
