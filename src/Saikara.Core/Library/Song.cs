namespace Saikara.Core.Library;

/// <summary>
/// A single song in the library. This is the platform-agnostic domain model that backs
/// the operator song-select remote and the reservation queue. It carries just enough
/// metadata to find a song and to locate its MIDI/KAR source for playback and scoring.
/// </summary>
public sealed record Song
{
    /// <summary>
    /// Stable database identity (autoincrement primary key). Zero means "not yet stored";
    /// a persisted <see cref="Song"/> always has a positive <see cref="Id"/>.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Karaoke selection number entered on the song-select remote (e.g. a DAM-style code).
    /// This is the natural key: it is unique within the library and is what
    /// <see cref="ISongLibrary.UpsertAsync"/> matches on.
    /// </summary>
    public required string Number { get; init; }

    /// <summary>Song title, shown in search results and the reservation queue.</summary>
    public required string Title { get; init; }

    /// <summary>Performing artist, shown in search results and used for artist search.</summary>
    public required string Artist { get; init; }

    /// <summary>Path to the backing Standard MIDI File (incl. <c>.kar</c> / Yamaha XF).</summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Index of the track holding the reference melody, used for the guide melody and
    /// scoring. <see langword="null"/> when the melody track has not been identified yet
    /// (the correction editor sets this later).
    /// </summary>
    public int? MelodyTrackIndex { get; init; }

    /// <summary>When the song was added to the library.</summary>
    public DateTimeOffset DateAdded { get; init; }
}
