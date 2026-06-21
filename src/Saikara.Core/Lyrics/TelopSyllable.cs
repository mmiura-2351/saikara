namespace Saikara.Core.Lyrics;

/// <summary>
/// One wipe segment of a telop line: a chunk of text whose color-wipe (the "already sung"
/// highlight) reaches it at <see cref="StartTime"/>. The segment is considered complete at the
/// next syllable's <see cref="StartTime"/>, or — for the last syllable of a line — at the line's
/// <see cref="TelopLine.EndTime"/>. A syllable is whatever granularity the source KAR/MIDI uses:
/// often a single mora/character, sometimes a whole word.
/// </summary>
public sealed record TelopSyllable
{
    /// <summary>The displayed text of this segment (with all markers and control characters stripped).</summary>
    public required string Text { get; init; }

    /// <summary>The metric (wall-clock) time, from the start of the song, at which the wipe reaches this segment.</summary>
    public required TimeSpan StartTime { get; init; }
}
