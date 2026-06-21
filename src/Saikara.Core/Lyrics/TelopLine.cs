namespace Saikara.Core.Lyrics;

/// <summary>
/// One displayable line of telop: a run of <see cref="TelopSyllable"/> segments that a
/// color-wipe sweeps across between <see cref="StartTime"/> and <see cref="EndTime"/>. Lines are
/// produced by <see cref="LyricTelopBuilder"/> by grouping raw lyric fragments at the
/// new-line (<c>/</c>), new-page (<c>\</c>) and embedded CR/LF break points.
/// </summary>
public sealed record TelopLine
{
    /// <summary>The time the line begins — equal to the first syllable's <see cref="TelopSyllable.StartTime"/>.</summary>
    public required TimeSpan StartTime { get; init; }

    /// <summary>
    /// The time the line ends: the next line's <see cref="StartTime"/>, or — for the final line —
    /// the last syllable's <see cref="TelopSyllable.StartTime"/>. The wipe reaches the right edge
    /// at this time.
    /// </summary>
    public required TimeSpan EndTime { get; init; }

    /// <summary>The full line text: the concatenation of every syllable's <see cref="TelopSyllable.Text"/>.</summary>
    public required string Text { get; init; }

    /// <summary>The line's wipe segments in order. Always non-empty for a built line.</summary>
    public required IReadOnlyList<TelopSyllable> Syllables { get; init; }

    /// <summary>
    /// <see langword="true"/> when this line opened a new paragraph/page (a <c>\</c> marker or an
    /// embedded page break), as opposed to a plain new line (<c>/</c> / CR / LF). The display can
    /// use this to clear the screen or animate a page turn; for a simple two-line telop it can be
    /// ignored.
    /// </summary>
    public bool StartsNewPage { get; init; }
}
