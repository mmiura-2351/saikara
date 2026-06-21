namespace Saikara.Core.Editor;

/// <summary>
/// A per-syllable timing tweak: shifts the lyric event at <see cref="LyricIndex"/> by
/// <see cref="DeltaMs"/> milliseconds. Positive values delay the syllable; negative values
/// advance it. Applied on top of the global <see cref="SongCorrections.LyricOffsetMs"/>.
/// </summary>
/// <param name="LyricIndex">
/// Zero-based index into the raw <see cref="Midi.MidiSong.Lyrics"/> list identifying the
/// lyric event to adjust.
/// </param>
/// <param name="DeltaMs">
/// Time shift in milliseconds. Positive delays the syllable; negative advances it.
/// The final time is clamped to &gt;= 0.
/// </param>
public readonly record struct SyllableAdjustment(int LyricIndex, double DeltaMs);
