using Saikara.Core.Editor;

namespace Saikara.Core.Tests.Editor;

/// <summary>Tests for <see cref="SongCorrections"/> construction and defaults.</summary>
public sealed class SongCorrectionsTests
{
    [Fact]
    public void Default_MelodyTrackIndex_IsNull()
    {
        var corrections = new SongCorrections();
        Assert.Null(corrections.MelodyTrackIndex);
    }

    [Fact]
    public void Default_LyricOffsetMs_IsZero()
    {
        var corrections = new SongCorrections();
        Assert.Equal(0.0, corrections.LyricOffsetMs);
    }

    [Fact]
    public void Default_PerSyllableAdjustments_IsNull()
    {
        var corrections = new SongCorrections();
        Assert.Null(corrections.PerSyllableAdjustments);
    }

    [Fact]
    public void Can_Set_MelodyTrackIndex()
    {
        var corrections = new SongCorrections { MelodyTrackIndex = 3 };
        Assert.Equal(3, corrections.MelodyTrackIndex);
    }

    [Fact]
    public void Can_Set_LyricOffsetMs()
    {
        var corrections = new SongCorrections { LyricOffsetMs = -150.5 };
        Assert.Equal(-150.5, corrections.LyricOffsetMs);
    }

    [Fact]
    public void Can_Set_PerSyllableAdjustments()
    {
        var adjustments = new[]
        {
            new SyllableAdjustment(0, 10.0),
            new SyllableAdjustment(5, -20.0),
        };

        var corrections = new SongCorrections { PerSyllableAdjustments = adjustments };

        Assert.NotNull(corrections.PerSyllableAdjustments);
        Assert.Equal(2, corrections.PerSyllableAdjustments.Count);
        Assert.Equal(0, corrections.PerSyllableAdjustments[0].LyricIndex);
        Assert.Equal(10.0, corrections.PerSyllableAdjustments[0].DeltaMs);
        Assert.Equal(5, corrections.PerSyllableAdjustments[1].LyricIndex);
        Assert.Equal(-20.0, corrections.PerSyllableAdjustments[1].DeltaMs);
    }

    [Fact]
    public void Record_Equality_By_Value()
    {
        var a = new SongCorrections { MelodyTrackIndex = 2, LyricOffsetMs = 50.0 };
        var b = new SongCorrections { MelodyTrackIndex = 2, LyricOffsetMs = 50.0 };
        Assert.Equal(a, b);
    }

    [Fact]
    public void SyllableAdjustment_Is_ValueType()
    {
        var a = new SyllableAdjustment(1, 5.0);
        var b = new SyllableAdjustment(1, 5.0);
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void SyllableAdjustment_Different_Values_NotEqual()
    {
        var a = new SyllableAdjustment(1, 5.0);
        var b = new SyllableAdjustment(2, 5.0);
        Assert.NotEqual(a, b);
    }
}
