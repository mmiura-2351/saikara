using Saikara.Core.Lyrics;

namespace Saikara.Core.Tests.Lyrics;

public class TelopPlaybackTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static TelopSyllable Syl(string text, double seconds)
        => new() { Text = text, StartTime = S(seconds) };

    /// <summary>
    /// Two lines:
    ///   line 0: [1.0 .. 3.0) "ab"  (a@1.0, b@2.0)
    ///   line 1: [3.0 .. 4.0] "cd"  (c@3.0, d@3.5), final end == 4.0
    /// </summary>
    private static TelopPlayback BuildPlayback()
    {
        var lines = new[]
        {
            new TelopLine
            {
                StartTime = S(1.0),
                EndTime = S(3.0),
                Text = "ab",
                Syllables = new[] { Syl("a", 1.0), Syl("b", 2.0) },
            },
            new TelopLine
            {
                StartTime = S(3.0),
                EndTime = S(4.0),
                Text = "cd",
                Syllables = new[] { Syl("c", 3.0), Syl("d", 3.5) },
            },
        };
        return new TelopPlayback(lines);
    }

    // --- ActiveLineIndex ---

    [Fact]
    public void ActiveLineIndex_BeforeFirstLine_IsMinusOne()
    {
        Assert.Equal(-1, BuildPlayback().ActiveLineIndex(S(0.5)));
    }

    [Fact]
    public void ActiveLineIndex_AtFirstLineStart_IsZero()
    {
        Assert.Equal(0, BuildPlayback().ActiveLineIndex(S(1.0)));
    }

    [Fact]
    public void ActiveLineIndex_MidFirstLine_IsZero()
    {
        Assert.Equal(0, BuildPlayback().ActiveLineIndex(S(2.5)));
    }

    [Fact]
    public void ActiveLineIndex_AtSecondLineStart_IsOne()
    {
        // 3.0 is the boundary: it belongs to line 1 (its start), not line 0.
        Assert.Equal(1, BuildPlayback().ActiveLineIndex(S(3.0)));
    }

    [Fact]
    public void ActiveLineIndex_AfterLastLineEnd_IsLastIndex()
    {
        // After the final line ends, it remains the active (last-shown) line.
        Assert.Equal(1, BuildPlayback().ActiveLineIndex(S(10.0)));
    }

    [Fact]
    public void ActiveLineIndex_EmptyPlayback_IsMinusOne()
    {
        var playback = new TelopPlayback(Array.Empty<TelopLine>());
        Assert.Equal(-1, playback.ActiveLineIndex(S(1.0)));
    }

    // --- WipeFraction ---

    [Fact]
    public void WipeFraction_BeforeFirstLine_IsZero()
    {
        Assert.Equal(0.0, BuildPlayback().WipeFraction(S(0.0)));
    }

    [Fact]
    public void WipeFraction_AtLineStart_IsZero()
    {
        Assert.Equal(0.0, BuildPlayback().WipeFraction(S(1.0)));
    }

    [Fact]
    public void WipeFraction_MidLine_Interpolates()
    {
        // Line 0 spans 1.0..3.0; at 2.0 the wipe is half-way.
        Assert.Equal(0.5, BuildPlayback().WipeFraction(S(2.0)), 6);
    }

    [Fact]
    public void WipeFraction_AtLineEnd_IsOne()
    {
        // 2.999 within line 0, near the end.
        Assert.Equal(0.9995, BuildPlayback().WipeFraction(S(2.999)), 3);
    }

    [Fact]
    public void WipeFraction_AfterFinalLineEnd_IsClampedToOne()
    {
        Assert.Equal(1.0, BuildPlayback().WipeFraction(S(10.0)));
    }

    // --- ElapsedSyllableCount + WipedText ---

    [Fact]
    public void ElapsedSyllableCount_CountsReachedSyllables()
    {
        TelopPlayback p = BuildPlayback();
        Assert.Equal(0, p.ElapsedSyllableCount(S(0.5)));   // before line
        Assert.Equal(1, p.ElapsedSyllableCount(S(1.0)));   // a reached
        Assert.Equal(1, p.ElapsedSyllableCount(S(1.9)));   // still on a
        Assert.Equal(2, p.ElapsedSyllableCount(S(2.0)));   // b reached
        Assert.Equal(2, p.ElapsedSyllableCount(S(2.9)));   // still line 0
    }

    [Fact]
    public void WipedText_ReturnsAlreadySungPrefix()
    {
        TelopPlayback p = BuildPlayback();
        Assert.Equal(string.Empty, p.WipedText(S(0.5)));
        Assert.Equal("a", p.WipedText(S(1.5)));
        Assert.Equal("ab", p.WipedText(S(2.5)));
    }

    [Fact]
    public void TryGetActiveLine_BeforeFirst_ReturnsFalse()
    {
        Assert.False(BuildPlayback().TryGetActiveLine(S(0.5), out _));
    }

    [Fact]
    public void TryGetActiveLine_MidLine_ReturnsLine()
    {
        Assert.True(BuildPlayback().TryGetActiveLine(S(2.0), out TelopLine? line));
        Assert.Equal("ab", line!.Text);
    }

    [Fact]
    public void Lines_ExposesUnderlyingLines()
    {
        Assert.Equal(2, BuildPlayback().Lines.Count);
    }

    [Fact]
    public void Constructor_NullLines_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TelopPlayback(null!));
    }
}
