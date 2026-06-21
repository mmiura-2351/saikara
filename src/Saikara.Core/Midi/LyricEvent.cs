namespace Saikara.Core.Midi;

/// <summary>
/// A timed text payload extracted from a Standard MIDI File. KAR/XF lyrics live in
/// <c>Lyric</c> and <c>Text</c> meta events; this captures them as (time, text) pairs
/// for the lyric-telop feature (P2). The raw text is preserved verbatim (including any
/// <c>/</c> line-break or <c>\</c> page-break markers and leading spaces) so the telop
/// layer can apply its own segmentation rules later.
/// </summary>
public sealed record LyricEvent
{
    /// <summary>Event time in MIDI ticks from the start of the song.</summary>
    public required long TimeTicks { get; init; }

    /// <summary>Event time as metric (wall-clock) time from the start of the song.</summary>
    public required TimeSpan Time { get; init; }

    /// <summary>The verbatim text payload of the meta event.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// <see langword="true"/> if this came from a <c>Lyric</c> meta event, <see langword="false"/>
    /// if from a generic <c>Text</c> meta event. Some KAR files put lyrics in <c>Text</c> events,
    /// so both are collected; this flag lets the telop layer prefer one source.
    /// </summary>
    public required bool IsLyric { get; init; }
}
