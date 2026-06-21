using Saikara.Core.Editor;
using Saikara.Core.Midi;

namespace Saikara.Core.Tests.Editor;

/// <summary>Tests for <see cref="CorrectedSongBuilder"/>.</summary>
public sealed class CorrectedSongBuilderTests
{
    /// <summary>Creates a minimal <see cref="MidiSong"/> with the given lyrics.</summary>
    private static MidiSong CreateSong(params LyricEvent[] lyrics)
    {
        return new MidiSong
        {
            TicksPerQuarterNote = 480,
            Duration = TimeSpan.FromMinutes(3),
            TempoChanges = new[]
            {
                new TempoChange
                {
                    TimeTicks = 0,
                    Time = TimeSpan.Zero,
                    MicrosecondsPerQuarterNote = 500_000, // 120 BPM
                },
            },
            Tracks = new[]
            {
                new MidiTrack
                {
                    Name = "Melody",
                    Channels = new[] { 0 },
                    Notes = new[]
                    {
                        new MidiNote
                        {
                            NoteNumber = 60,
                            Channel = 0,
                            Velocity = 100,
                            StartTicks = 0,
                            LengthTicks = 480,
                            StartTime = TimeSpan.Zero,
                            Length = TimeSpan.FromMilliseconds(500),
                        },
                    },
                },
                new MidiTrack
                {
                    Name = "Accompaniment",
                    Channels = new[] { 1 },
                    Notes = new[]
                    {
                        new MidiNote
                        {
                            NoteNumber = 48,
                            Channel = 1,
                            Velocity = 80,
                            StartTicks = 0,
                            LengthTicks = 960,
                            StartTime = TimeSpan.Zero,
                            Length = TimeSpan.FromSeconds(1),
                        },
                    },
                },
            },
            Lyrics = lyrics,
        };
    }

    private static LyricEvent MakeLyric(double timeMs, string text)
    {
        return new LyricEvent
        {
            TimeTicks = (long)(timeMs * 0.96), // approximate, not important for these tests
            Time = TimeSpan.FromMilliseconds(timeMs),
            Text = text,
            IsLyric = true,
        };
    }

    [Fact]
    public void NullCorrections_Returns_Unchanged()
    {
        var song = CreateSong(
            MakeLyric(0, "Hello"),
            MakeLyric(1000, "World"));

        var result = CorrectedSongBuilder.Apply(song, null);

        Assert.Same(song, result.Song);
        Assert.Same(song.Lyrics, result.Lyrics);
        Assert.Null(result.MelodyTrackIndex);
    }

    [Fact]
    public void NullSong_Throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CorrectedSongBuilder.Apply(null!, null));
    }

    [Fact]
    public void GlobalOffset_Positive_Delays_Lyrics()
    {
        var song = CreateSong(
            MakeLyric(500, "A"),
            MakeLyric(1000, "B"));

        var corrections = new SongCorrections { LyricOffsetMs = 200 };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        Assert.Equal(TimeSpan.FromMilliseconds(700), result.Lyrics[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), result.Lyrics[1].Time);
    }

    [Fact]
    public void GlobalOffset_Negative_Advances_Lyrics()
    {
        var song = CreateSong(
            MakeLyric(500, "A"),
            MakeLyric(1000, "B"));

        var corrections = new SongCorrections { LyricOffsetMs = -300 };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        Assert.Equal(TimeSpan.FromMilliseconds(200), result.Lyrics[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(700), result.Lyrics[1].Time);
    }

    [Fact]
    public void GlobalOffset_Clamps_To_Zero()
    {
        var song = CreateSong(
            MakeLyric(100, "A"),
            MakeLyric(1000, "B"));

        var corrections = new SongCorrections { LyricOffsetMs = -500 };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        // 100 - 500 = -400 -> clamped to 0
        Assert.Equal(TimeSpan.Zero, result.Lyrics[0].Time);
        // 1000 - 500 = 500 -> no clamping
        Assert.Equal(TimeSpan.FromMilliseconds(500), result.Lyrics[1].Time);
    }

    [Fact]
    public void PerSyllable_Adjustments_Applied()
    {
        var song = CreateSong(
            MakeLyric(500, "A"),
            MakeLyric(1000, "B"),
            MakeLyric(1500, "C"));

        var corrections = new SongCorrections
        {
            PerSyllableAdjustments = new[]
            {
                new SyllableAdjustment(0, 50.0),   // A: 500 + 50 = 550
                new SyllableAdjustment(2, -100.0),  // C: 1500 - 100 = 1400
            },
        };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        Assert.Equal(TimeSpan.FromMilliseconds(550), result.Lyrics[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), result.Lyrics[1].Time); // B unchanged
        Assert.Equal(TimeSpan.FromMilliseconds(1400), result.Lyrics[2].Time);
    }

    [Fact]
    public void GlobalOffset_Plus_PerSyllable_Are_Combined()
    {
        var song = CreateSong(
            MakeLyric(1000, "A"),
            MakeLyric(2000, "B"));

        var corrections = new SongCorrections
        {
            LyricOffsetMs = 100, // global +100
            PerSyllableAdjustments = new[]
            {
                new SyllableAdjustment(0, -50.0), // A: 1000 + 100 - 50 = 1050
            },
        };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        Assert.Equal(TimeSpan.FromMilliseconds(1050), result.Lyrics[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(2100), result.Lyrics[1].Time); // B: 2000 + 100
    }

    [Fact]
    public void PerSyllable_Clamped_To_Zero()
    {
        var song = CreateSong(
            MakeLyric(50, "A"));

        var corrections = new SongCorrections
        {
            PerSyllableAdjustments = new[]
            {
                new SyllableAdjustment(0, -200.0), // 50 - 200 = -150 -> 0
            },
        };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        Assert.Equal(TimeSpan.Zero, result.Lyrics[0].Time);
    }

    [Fact]
    public void MelodyTrackIndex_Passthrough()
    {
        var song = CreateSong(MakeLyric(0, "A"));

        var corrections = new SongCorrections { MelodyTrackIndex = 1 };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        Assert.Equal(1, result.MelodyTrackIndex);
    }

    [Fact]
    public void MelodyTrackIndex_Null_Passthrough()
    {
        var song = CreateSong(MakeLyric(0, "A"));

        var corrections = new SongCorrections { MelodyTrackIndex = null };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        Assert.Null(result.MelodyTrackIndex);
    }

    [Fact]
    public void ZeroOffset_NoAdjustments_Lyrics_Unchanged()
    {
        var song = CreateSong(
            MakeLyric(500, "A"),
            MakeLyric(1000, "B"));

        var corrections = new SongCorrections
        {
            LyricOffsetMs = 0.0,
            PerSyllableAdjustments = null,
        };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        // Should return the original list (same reference) since no shifts needed.
        Assert.Same(song.Lyrics, result.Lyrics);
    }

    [Fact]
    public void Lyric_Text_And_IsLyric_Are_Preserved()
    {
        var song = CreateSong(
            new LyricEvent
            {
                TimeTicks = 0,
                Time = TimeSpan.FromMilliseconds(500),
                Text = "/Hello World",
                IsLyric = false,
            });

        var corrections = new SongCorrections { LyricOffsetMs = 100 };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        Assert.Equal("/Hello World", result.Lyrics[0].Text);
        Assert.False(result.Lyrics[0].IsLyric);
        Assert.Equal(TimeSpan.FromMilliseconds(600), result.Lyrics[0].Time);
    }

    [Fact]
    public void Lyric_TimeTicks_Is_Preserved_On_Shift()
    {
        var song = CreateSong(
            new LyricEvent
            {
                TimeTicks = 480,
                Time = TimeSpan.FromMilliseconds(500),
                Text = "test",
                IsLyric = true,
            });

        var corrections = new SongCorrections { LyricOffsetMs = 100 };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        // TimeTicks is not adjusted (it's the raw MIDI tick value);
        // only the metric Time is shifted.
        Assert.Equal(480, result.Lyrics[0].TimeTicks);
        Assert.Equal(TimeSpan.FromMilliseconds(600), result.Lyrics[0].Time);
    }

    [Fact]
    public void Original_Song_Is_Not_Mutated()
    {
        var song = CreateSong(
            MakeLyric(500, "A"),
            MakeLyric(1000, "B"));

        var corrections = new SongCorrections { LyricOffsetMs = 200 };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        // Original song lyrics unchanged.
        Assert.Equal(TimeSpan.FromMilliseconds(500), song.Lyrics[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), song.Lyrics[1].Time);

        // Result is different.
        Assert.Equal(TimeSpan.FromMilliseconds(700), result.Lyrics[0].Time);
    }

    [Fact]
    public void Empty_Lyrics_With_Offset_Returns_Empty()
    {
        var song = CreateSong(); // no lyrics

        var corrections = new SongCorrections { LyricOffsetMs = 100 };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        Assert.Empty(result.Lyrics);
    }

    [Fact]
    public void Song_Reference_Is_Preserved()
    {
        var song = CreateSong(MakeLyric(0, "A"));

        var corrections = new SongCorrections { MelodyTrackIndex = 1, LyricOffsetMs = 50 };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        Assert.Same(song, result.Song);
    }

    [Fact]
    public void PerSyllable_OutOfRange_Index_Is_Ignored()
    {
        var song = CreateSong(
            MakeLyric(500, "A"),
            MakeLyric(1000, "B"));

        // Index 99 is beyond the lyrics list; it should be silently ignored.
        var corrections = new SongCorrections
        {
            PerSyllableAdjustments = new[]
            {
                new SyllableAdjustment(99, 100.0),
            },
        };

        var result = CorrectedSongBuilder.Apply(song, corrections);

        // Lyrics should be unchanged (the out-of-range adjustment is ignored).
        Assert.Equal(TimeSpan.FromMilliseconds(500), result.Lyrics[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), result.Lyrics[1].Time);
    }
}
