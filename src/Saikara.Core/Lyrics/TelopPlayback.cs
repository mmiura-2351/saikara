namespace Saikara.Core.Lyrics;

/// <summary>
/// A pure, deterministic playback helper over a built set of <see cref="TelopLine"/>s. Given the
/// current playback position (e.g. <c>IAudioEngine.Position</c>) it answers which line is active
/// and how far the color-wipe has advanced, so the display window can render a two-line color-wipe
/// telop in sync with the audio. It holds no mutable state and performs no timing of its own.
/// </summary>
public sealed class TelopPlayback
{
    private readonly IReadOnlyList<TelopLine> _lines;

    /// <summary>Creates a playback helper over the given ordered telop lines.</summary>
    /// <param name="lines">Telop lines in time order (as produced by <see cref="LyricTelopBuilder"/>).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is <see langword="null"/>.</exception>
    public TelopPlayback(IReadOnlyList<TelopLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _lines = lines;
    }

    /// <summary>The underlying telop lines, in time order.</summary>
    public IReadOnlyList<TelopLine> Lines => _lines;

    /// <summary>
    /// The index of the line that should be highlighted at <paramref name="position"/>, or
    /// <c>-1</c> before the first line starts (or when there are no lines). The boundary at a
    /// line's <see cref="TelopLine.StartTime"/> belongs to that line. After the final line ends,
    /// the final line stays active (it remains the last thing shown).
    /// </summary>
    /// <param name="position">The current playback position from the start of the song.</param>
    public int ActiveLineIndex(TimeSpan position)
    {
        if (_lines.Count == 0 || position < _lines[0].StartTime)
        {
            return -1;
        }

        // Find the last line whose start is at or before the position.
        int index = 0;
        for (int i = 1; i < _lines.Count; i++)
        {
            if (_lines[i].StartTime <= position)
            {
                index = i;
            }
            else
            {
                break;
            }
        }

        return index;
    }

    /// <summary>
    /// Gets the active line at <paramref name="position"/>.
    /// </summary>
    /// <param name="position">The current playback position from the start of the song.</param>
    /// <param name="line">The active line, or <see langword="null"/> when none is active.</param>
    /// <returns><see langword="true"/> when a line is active (the wipe has begun); otherwise <see langword="false"/>.</returns>
    public bool TryGetActiveLine(TimeSpan position, out TelopLine? line)
    {
        int index = ActiveLineIndex(position);
        if (index < 0)
        {
            line = null;
            return false;
        }

        line = _lines[index];
        return true;
    }

    /// <summary>
    /// The wipe progress within the active line as a fraction in <c>[0, 1]</c>: how far the
    /// color-wipe has swept across the line's text (by time). Returns <c>0</c> before the first
    /// line and is clamped to <c>1</c> at/after a line's <see cref="TelopLine.EndTime"/>.
    /// </summary>
    /// <param name="position">The current playback position from the start of the song.</param>
    public double WipeFraction(TimeSpan position)
    {
        int index = ActiveLineIndex(position);
        if (index < 0)
        {
            return 0.0;
        }

        TelopLine line = _lines[index];
        double span = (line.EndTime - line.StartTime).TotalSeconds;
        if (span <= 0.0)
        {
            // Zero-length line: it is fully wiped as soon as it is reached.
            return position >= line.StartTime ? 1.0 : 0.0;
        }

        double elapsed = (position - line.StartTime).TotalSeconds;
        return Math.Clamp(elapsed / span, 0.0, 1.0);
    }

    /// <summary>
    /// The number of syllables in the active line whose <see cref="TelopSyllable.StartTime"/> has
    /// been reached at <paramref name="position"/> (i.e. fully or partially sung). <c>0</c> when no
    /// line is active. Useful for a per-syllable (step) wipe.
    /// </summary>
    /// <param name="position">The current playback position from the start of the song.</param>
    public int ElapsedSyllableCount(TimeSpan position)
    {
        int index = ActiveLineIndex(position);
        if (index < 0)
        {
            return 0;
        }

        TelopLine line = _lines[index];
        int count = 0;
        foreach (TelopSyllable syllable in line.Syllables)
        {
            if (syllable.StartTime <= position)
            {
                count++;
            }
            else
            {
                break;
            }
        }

        return count;
    }

    /// <summary>
    /// The already-sung prefix of the active line: the concatenated text of the syllables whose
    /// <see cref="TelopSyllable.StartTime"/> has been reached at <paramref name="position"/>.
    /// Returns <see cref="string.Empty"/> when no line is active. This gives a step (per-syllable)
    /// view of the wipe; for a smooth wipe use <see cref="WipeFraction(TimeSpan)"/>.
    /// </summary>
    /// <param name="position">The current playback position from the start of the song.</param>
    public string WipedText(TimeSpan position)
    {
        int index = ActiveLineIndex(position);
        if (index < 0)
        {
            return string.Empty;
        }

        int reached = ElapsedSyllableCount(position);
        if (reached == 0)
        {
            return string.Empty;
        }

        return string.Concat(_lines[index].Syllables.Take(reached).Select(s => s.Text));
    }
}
