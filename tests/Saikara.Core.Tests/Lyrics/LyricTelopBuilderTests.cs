using Saikara.Core.Lyrics;
using Saikara.Core.Midi;

namespace Saikara.Core.Tests.Lyrics;

public class LyricTelopBuilderTests
{
    private static readonly TimeSpan Tempo120Quarter = TimeSpan.FromMilliseconds(500);

    /// <summary>Builds a <see cref="LyricEvent"/> at the given metric time (ticks are irrelevant to the telop layer).</summary>
    private static LyricEvent Ev(string text, double seconds, bool isLyric = true)
        => new()
        {
            TimeTicks = (long)(seconds * 1000),
            Time = TimeSpan.FromSeconds(seconds),
            Text = text,
            IsLyric = isLyric,
        };

    /// <summary>Wraps a list of lyric events in a minimal <see cref="MidiSong"/>.</summary>
    private static MidiSong SongWith(params LyricEvent[] lyrics)
        => new()
        {
            TicksPerQuarterNote = 480,
            Duration = TimeSpan.FromSeconds(60),
            TempoChanges = new[]
            {
                new TempoChange
                {
                    TimeTicks = 0,
                    Time = TimeSpan.Zero,
                    MicrosecondsPerQuarterNote = 500_000,
                },
            },
            Tracks = Array.Empty<MidiTrack>(),
            Lyrics = lyrics,
        };

    private static IReadOnlyList<TelopLine> Build(params LyricEvent[] lyrics)
        => new LyricTelopBuilder().Build(SongWith(lyrics));

    // --- Empty / no usable lyrics ---

    [Fact]
    public void Build_NoLyrics_ReturnsEmpty()
    {
        Assert.Empty(Build());
    }

    [Fact]
    public void Build_OnlyMetadataAndWhitespace_ReturnsEmpty()
    {
        var lines = Build(
            Ev("@KMIDI KARAOKE FILE", 0.0),
            Ev("@L ENGLISH", 0.1),
            Ev("@T Title", 0.2),
            Ev("   ", 0.3),
            Ev("\r\n", 0.4));

        Assert.Empty(lines);
    }

    [Fact]
    public void Build_NullSong_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LyricTelopBuilder().Build((MidiSong)null!));
    }

    // --- Basic grouping ---

    [Fact]
    public void Build_FragmentsOnOneLine_AreOneTelopLine()
    {
        var lines = Build(
            Ev("Twin", 1.0),
            Ev("kle", 1.5),
            Ev("twin", 2.0),
            Ev("kle", 2.5));

        TelopLine line = Assert.Single(lines);
        Assert.Equal("Twinkletwinkle", line.Text);
        Assert.Equal(4, line.Syllables.Count);
        Assert.Equal(new[] { "Twin", "kle", "twin", "kle" }, line.Syllables.Select(s => s.Text));
    }

    [Fact]
    public void Build_SyllableStartTimesArePreserved()
    {
        var lines = Build(
            Ev("a", 1.0),
            Ev("b", 1.25),
            Ev("c", 1.75));

        TelopSyllable[] syllables = lines.Single().Syllables.ToArray();
        Assert.Equal(TimeSpan.FromSeconds(1.0), syllables[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(1.25), syllables[1].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(1.75), syllables[2].StartTime);
    }

    // --- New-line marker '/' ---

    [Fact]
    public void Build_SlashStartsNewLine_AndIsStripped()
    {
        var lines = Build(
            Ev("Hel", 1.0),
            Ev("lo", 1.5),
            Ev("/world", 2.0),
            Ev("!", 2.5));

        Assert.Equal(2, lines.Count);
        Assert.Equal("Hello", lines[0].Text);
        Assert.Equal("world!", lines[1].Text);
        // Marker stripped, but the syllable text itself is kept.
        Assert.Equal("world", lines[1].Syllables[0].Text);
    }

    // --- New-page marker '\' ---

    [Fact]
    public void Build_BackslashStartsNewLine_AndIsStripped()
    {
        var lines = Build(
            Ev("page", 1.0),
            Ev("one", 1.5),
            Ev("\\page", 2.0),
            Ev("two", 2.5));

        Assert.Equal(2, lines.Count);
        Assert.Equal("pageone", lines[0].Text);
        Assert.Equal("pagetwo", lines[1].Text);
        Assert.True(lines[1].StartsNewPage);
        Assert.False(lines[0].StartsNewPage);
    }

    [Fact]
    public void Build_FirstLineFromSlashOrBackslash_HasNoEmptyLeadingLine()
    {
        var lines = Build(
            Ev("\\first", 1.0),
            Ev("/second", 2.0));

        Assert.Equal(2, lines.Count);
        Assert.Equal("first", lines[0].Text);
        Assert.Equal("second", lines[1].Text);
    }

    // --- Embedded CR / LF ---

    [Fact]
    public void Build_EmbeddedLineFeed_BreaksLine()
    {
        var lines = Build(
            Ev("foo\n", 1.0),
            Ev("bar", 2.0));

        Assert.Equal(2, lines.Count);
        Assert.Equal("foo", lines[0].Text);
        Assert.Equal("bar", lines[1].Text);
    }

    [Fact]
    public void Build_EmbeddedCarriageReturnLineFeed_BreaksOncePerFragment()
    {
        var lines = Build(
            Ev("foo\r\n", 1.0),
            Ev("bar", 2.0));

        Assert.Equal(2, lines.Count);
        Assert.Equal("foo", lines[0].Text);
        Assert.Equal("bar", lines[1].Text);
    }

    [Fact]
    public void Build_OtherControlCharacters_AreStripped()
    {
        // Embedded tab / bell are stripped from the syllable text without splitting the line.
        var lines = Build(
            Ev("a\tb", 1.0),
            Ev("c", 2.0));

        TelopLine line = Assert.Single(lines);
        Assert.Equal("abc", line.Text);
    }

    // --- Metadata skipping mid-stream ---

    [Fact]
    public void Build_MetadataInterleaved_IsSkippedWithoutBreakingLines()
    {
        var lines = Build(
            Ev("a", 1.0),
            Ev("@L ENGLISH", 1.2),
            Ev("b", 1.5));

        TelopLine line = Assert.Single(lines);
        Assert.Equal("ab", line.Text);
        Assert.Equal(2, line.Syllables.Count);
    }

    // --- Line Start / End times ---

    [Fact]
    public void Build_LineStartIsFirstSyllable_EndIsNextLineStart()
    {
        var lines = Build(
            Ev("a", 1.0),
            Ev("b", 1.5),
            Ev("/c", 3.0),
            Ev("d", 3.5));

        Assert.Equal(TimeSpan.FromSeconds(1.0), lines[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(3.0), lines[0].EndTime);
        Assert.Equal(TimeSpan.FromSeconds(3.0), lines[1].StartTime);
    }

    [Fact]
    public void Build_FinalLineEnd_IsLastSyllableTime()
    {
        var lines = Build(
            Ev("a", 1.0),
            Ev("/b", 2.0),
            Ev("c", 2.5));

        // Final line: last syllable at 2.5 -> end == 2.5.
        Assert.Equal(TimeSpan.FromSeconds(2.0), lines[1].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(2.5), lines[1].EndTime);
    }

    [Fact]
    public void Build_TrailingBreaks_DoNotCreateEmptyTrailingLine()
    {
        var lines = Build(
            Ev("a", 1.0),
            Ev("b\n", 1.5),
            Ev("   ", 2.0),
            Ev("\\", 2.5));

        TelopLine line = Assert.Single(lines);
        Assert.Equal("ab", line.Text);
    }

    // --- Build from raw list overload ---

    [Fact]
    public void Build_FromLyricList_Works()
    {
        IReadOnlyList<TelopLine> lines = new LyricTelopBuilder().Build(
            new[] { Ev("x", 1.0), Ev("/y", 2.0) });

        Assert.Equal(2, lines.Count);
    }
}
