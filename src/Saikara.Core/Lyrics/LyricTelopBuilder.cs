using System.Text;
using Saikara.Core.Midi;

namespace Saikara.Core.Lyrics;

/// <summary>
/// Default <see cref="ILyricTelopBuilder"/>. Interprets the KAR/MIDI lyric conventions found in
/// each fragment of <see cref="MidiSong.Lyrics"/> and groups the fragments into
/// <see cref="TelopLine"/>s for a two-line color-wipe display.
/// </summary>
/// <remarks>
/// Per-fragment rules, applied in order:
/// <list type="bullet">
///   <item>A fragment beginning with <c>\</c> (backslash) starts a new paragraph/page (and a new
///   line); the marker is stripped and the resulting line is flagged
///   <see cref="TelopLine.StartsNewPage"/>.</item>
///   <item>A fragment beginning with <c>/</c> (slash) starts a new line; the marker is stripped.</item>
///   <item>Fragments beginning with <c>@</c> are KAR metadata (e.g. <c>@KMIDI KARAOKE FILE</c>,
///   <c>@L</c>, <c>@T...</c>) and are skipped entirely.</item>
///   <item>Embedded CR (<c>\r</c>) and LF (<c>\n</c>) are treated as line breaks; other control
///   characters are stripped. Text before such a break stays on the current line; text after it
///   begins a new line.</item>
///   <item>Fragments that are empty or whitespace-only after stripping are ignored.</item>
/// </list>
/// Each surviving piece of text becomes a <see cref="TelopSyllable"/> whose
/// <see cref="TelopSyllable.StartTime"/> is the originating event's <see cref="LyricEvent.Time"/>.
/// </remarks>
public sealed class LyricTelopBuilder : ILyricTelopBuilder
{
    /// <inheritdoc />
    public IReadOnlyList<TelopLine> Build(MidiSong song)
    {
        ArgumentNullException.ThrowIfNull(song);
        return Build(song.Lyrics);
    }

    /// <inheritdoc />
    public IReadOnlyList<TelopLine> Build(IReadOnlyList<LyricEvent> lyrics)
    {
        ArgumentNullException.ThrowIfNull(lyrics);

        // Pass 1: fragments -> syllables tagged with the break that precedes them. A break is
        // carried across fragment boundaries: a fragment ending in CR/LF (with no text after it)
        // forces the next emitted syllable onto a new line, even from a later fragment.
        var pending = new List<PendingSyllable>();
        BreakKind carriedBreak = BreakKind.None;
        foreach (LyricEvent ev in lyrics)
        {
            carriedBreak = AppendFragment(pending, ev.Text ?? string.Empty, ev.Time, carriedBreak);
        }

        if (pending.Count == 0)
        {
            return Array.Empty<TelopLine>();
        }

        // Pass 2: group syllables into lines at the break boundaries. The very first syllable
        // always opens a line regardless of its break flag, so a leading '/' or '\' does not
        // produce an empty leading line.
        var lineGroups = new List<LineGroup>();
        foreach (PendingSyllable syllable in pending)
        {
            bool startNewLine = lineGroups.Count == 0 || syllable.Break != BreakKind.None;
            if (startNewLine)
            {
                lineGroups.Add(new LineGroup(syllable.Break == BreakKind.Page));
            }

            lineGroups[^1].Syllables.Add(new TelopSyllable
            {
                Text = syllable.Text,
                StartTime = syllable.StartTime,
            });
        }

        // Pass 3: materialize lines and compute their Start/End times.
        var lines = new List<TelopLine>(lineGroups.Count);
        for (int i = 0; i < lineGroups.Count; i++)
        {
            LineGroup group = lineGroups[i];
            TimeSpan start = group.Syllables[0].StartTime;
            TimeSpan end = i + 1 < lineGroups.Count
                ? lineGroups[i + 1].Syllables[0].StartTime
                : group.Syllables[^1].StartTime;

            lines.Add(new TelopLine
            {
                StartTime = start,
                EndTime = end,
                Text = string.Concat(group.Syllables.Select(s => s.Text)),
                Syllables = group.Syllables,
                StartsNewPage = group.StartsNewPage,
            });
        }

        return lines;
    }

    /// <summary>
    /// Parses a single raw fragment, emitting zero or more <see cref="PendingSyllable"/>s into
    /// <paramref name="sink"/>. Leading <c>/</c> / <c>\</c> markers and embedded CR/LF set the
    /// break kind of the syllable that follows; other control characters are stripped;
    /// <c>@</c>-metadata and whitespace-only results are dropped.
    /// </summary>
    /// <param name="carriedBreak">
    /// A break left pending by an earlier fragment (e.g. one that ended with CR/LF). It is merged
    /// into this fragment's leading break and applied to the first syllable emitted here.
    /// </param>
    /// <returns>
    /// The break still pending after this fragment — non-<see cref="BreakKind.None"/> when the
    /// fragment introduced a break but emitted no following text, so the next syllable starts a
    /// new line.
    /// </returns>
    private static BreakKind AppendFragment(
        List<PendingSyllable> sink, string raw, TimeSpan time, BreakKind carriedBreak)
    {
        // KAR metadata (e.g. "@KMIDI KARAOKE FILE", "@L", "@T...") is never shown, and a leading
        // metadata fragment must not consume a pending break — keep carrying it.
        if (raw.StartsWith('@'))
        {
            return carriedBreak;
        }

        int index = 0;

        // Consume a single leading line/page marker, if any, and merge it with any carried break.
        BreakKind pendingBreak = carriedBreak;
        if (index < raw.Length && (raw[index] == '\\' || raw[index] == '/'))
        {
            pendingBreak = Stronger(pendingBreak, raw[index] == '\\' ? BreakKind.Page : BreakKind.Line);
            index++;
        }

        // Walk the remainder: accumulate visible characters, split on CR/LF, strip other controls.
        var current = new StringBuilder();

        void Flush()
        {
            string text = current.ToString();
            current.Clear();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            sink.Add(new PendingSyllable(text, time, pendingBreak));
            pendingBreak = BreakKind.None;
        }

        for (; index < raw.Length; index++)
        {
            char c = raw[index];
            if (c == '\r' || c == '\n')
            {
                // CR/LF (or a CR+LF pair) ends the current segment and forces a new line for what
                // follows — within this fragment or in a later one.
                Flush();
                pendingBreak = Stronger(pendingBreak, BreakKind.Line);
                if (c == '\r' && index + 1 < raw.Length && raw[index + 1] == '\n')
                {
                    index++;
                }
                continue;
            }

            if (char.IsControl(c))
            {
                // Strip other control characters (tab, bell, etc.) without splitting.
                continue;
            }

            current.Append(c);
        }

        Flush();
        return pendingBreak;
    }

    /// <summary>Returns the stronger of two breaks (Page &gt; Line &gt; None).</summary>
    private static BreakKind Stronger(BreakKind a, BreakKind b) => (BreakKind)Math.Max((int)a, (int)b);

    /// <summary>The kind of break a syllable introduces relative to the previous one.</summary>
    private enum BreakKind
    {
        /// <summary>No break — continues the current line.</summary>
        None,

        /// <summary>Starts a new line (<c>/</c> or embedded CR/LF).</summary>
        Line,

        /// <summary>Starts a new paragraph/page, which is also a new line (<c>\</c>).</summary>
        Page,
    }

    private readonly record struct PendingSyllable(string Text, TimeSpan StartTime, BreakKind Break);

    private sealed class LineGroup
    {
        public LineGroup(bool startsNewPage)
        {
            StartsNewPage = startsNewPage;
        }

        public bool StartsNewPage { get; }

        public List<TelopSyllable> Syllables { get; } = new();
    }
}
