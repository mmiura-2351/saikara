namespace Saikara.Core.Editor;

/// <summary>
/// User-specified overrides that correct quality issues in a song's MIDI data. Each song
/// can have at most one <see cref="SongCorrections"/> instance, persisted via
/// <see cref="ISongCorrectionsStore"/> and applied by <see cref="CorrectedSongBuilder"/>
/// before the engine builds the telop or scoring reference.
/// </summary>
public sealed record SongCorrections
{
    /// <summary>
    /// Override for the auto-detected melody track index. When non-<see langword="null"/>,
    /// this value replaces whatever <see cref="Midi.MelodyTrackDetector"/> returned and is
    /// used by the guide melody, scoring and pitch bar. <see langword="null"/> means "keep
    /// the auto-detected value" (no override).
    /// </summary>
    public int? MelodyTrackIndex { get; init; }

    /// <summary>
    /// Global shift of all lyric event times by this many milliseconds. Positive values
    /// delay lyrics (they appear later); negative values advance them (they appear earlier).
    /// Applied before <see cref="PerSyllableAdjustments"/> and before
    /// <see cref="Lyrics.LyricTelopBuilder"/> processes the lyrics.
    /// </summary>
    public double LyricOffsetMs { get; init; }

    /// <summary>
    /// Optional per-syllable time tweaks, each identifying a lyric event by its index in
    /// <see cref="Midi.MidiSong.Lyrics"/> and a delta in milliseconds. Applied on top of
    /// <see cref="LyricOffsetMs"/>. <see langword="null"/> when no per-syllable adjustments
    /// have been set.
    /// </summary>
    public IReadOnlyList<SyllableAdjustment>? PerSyllableAdjustments { get; init; }
}
