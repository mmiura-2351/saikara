using Saikara.Core.Library;

namespace Saikara.Core.Import;

/// <summary>
/// The outcome of a successful MIDI/KAR import: the <see cref="Song"/> that was stored in the
/// library, plus a few facts derived while parsing the file that are useful to surface in the
/// import UI without re-reading the file.
/// </summary>
public sealed record ImportResult
{
    /// <summary>
    /// The song that was inserted or updated in the library. Its <see cref="Song.FilePath"/>
    /// points at the copy made inside the library directory, and its <see cref="Song.Id"/> is
    /// the persisted database id.
    /// </summary>
    public required Song Song { get; init; }

    /// <summary>
    /// <see langword="true"/> when the imported file carries at least one lyric/text event,
    /// i.e. it is usable for the lyric telop. <see langword="false"/> for a bare instrumental MIDI.
    /// </summary>
    public required bool HasLyrics { get; init; }

    /// <summary>
    /// The detected reference-melody track index (see
    /// <see cref="Midi.MelodyTrackDetector"/>), or <see langword="null"/> when none could be
    /// detected automatically. Mirrors <see cref="Song.MelodyTrackIndex"/>.
    /// </summary>
    public required int? MelodyTrackIndex { get; init; }

    /// <summary>The total playable duration of the imported song.</summary>
    public required TimeSpan Duration { get; init; }
}
