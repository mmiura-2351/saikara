using Saikara.Core.Midi;

namespace Saikara.Core.Tests.Midi;

public class MidiTransformsTests
{
    private const short Tpqn = 480;

    private static MidiSong Load(TestMidiBuilder builder)
        => new MidiLoader().Load(builder.BuildStream());

    // --- Transpose ---

    [Fact]
    public void Transpose_ShiftsPitch()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)
                .Concat(TestMidiBuilder.Note(64, 0, Tpqn, Tpqn))));

        MidiSong up = MidiTransforms.Transpose(song, 5);

        var notes = up.Tracks.SelectMany(t => t.Notes).Select(n => n.NoteNumber).ToList();
        Assert.Equal(new[] { 65, 69 }, notes);
    }

    [Fact]
    public void Transpose_ClampsAtUpperBound()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(125, 0, 0, Tpqn)));

        MidiSong up = MidiTransforms.Transpose(song, 10);

        Assert.Equal(127, up.Tracks.SelectMany(t => t.Notes).Single().NoteNumber);
    }

    [Fact]
    public void Transpose_ClampsAtLowerBound()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(2, 0, 0, Tpqn)));

        MidiSong down = MidiTransforms.Transpose(song, -10);

        Assert.Equal(0, down.Tracks.SelectMany(t => t.Notes).Single().NoteNumber);
    }

    [Fact]
    public void Transpose_SkipsPercussionChannel10()
    {
        // Channel index 9 == MIDI channel 10 (percussion): note numbers must NOT shift.
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)          // melodic
                .Concat(TestMidiBuilder.Note(38, 9, 0, Tpqn))));    // snare drum

        MidiSong up = MidiTransforms.Transpose(song, 7);

        var notes = up.Tracks.SelectMany(t => t.Notes).ToList();
        MidiNote melodic = notes.Single(n => n.Channel == 0);
        MidiNote drum = notes.Single(n => n.Channel == 9);

        Assert.Equal(67, melodic.NoteNumber); // shifted
        Assert.Equal(38, drum.NoteNumber);     // unchanged
    }

    [Fact]
    public void Transpose_DoesNotMutateOriginal()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        _ = MidiTransforms.Transpose(song, 12);

        Assert.Equal(60, song.Tracks.SelectMany(t => t.Notes).Single().NoteNumber);
    }

    [Fact]
    public void Transpose_ZeroSemitones_ReturnsEquivalent()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        MidiSong same = MidiTransforms.Transpose(song, 0);

        Assert.Equal(60, same.Tracks.SelectMany(t => t.Notes).Single().NoteNumber);
    }

    [Fact]
    public void Transpose_KeepsTiming()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, Tpqn, Tpqn)));

        MidiSong up = MidiTransforms.Transpose(song, 3);

        MidiNote note = up.Tracks.SelectMany(t => t.Notes).Single();
        Assert.Equal(Tpqn, note.StartTicks);
        Assert.Equal(500, note.StartTime.TotalMilliseconds, precision: 0);
        Assert.Equal(500, note.Length.TotalMilliseconds, precision: 0);
    }

    // --- Tempo scaling (percent) ---

    [Fact]
    public void ScaleTempo_150Percent_SpeedsUpAndShortens()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn * 4)));

        MidiSong fast = MidiTransforms.ScaleTempo(song, 150);

        Assert.Equal(180.0, fast.InitialBeatsPerMinute, precision: 1);
        // Original duration 2000 ms; 1.5x faster => ~1333 ms.
        Assert.Equal(2000.0 / 1.5, fast.Duration.TotalMilliseconds, precision: 0);
        // Ticks are unchanged; only metric times scale.
        MidiNote note = fast.Tracks.SelectMany(t => t.Notes).Single();
        Assert.Equal(Tpqn * 4, note.LengthTicks);
        Assert.Equal(2000.0 / 1.5, note.Length.TotalMilliseconds, precision: 0);
    }

    [Fact]
    public void ScaleTempo_50Percent_SlowsDownAndLengthens()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn * 4)));

        MidiSong slow = MidiTransforms.ScaleTempo(song, 50);

        Assert.Equal(60.0, slow.InitialBeatsPerMinute, precision: 1);
        Assert.Equal(4000, slow.Duration.TotalMilliseconds, precision: 0);
    }

    [Fact]
    public void ScaleTempo_100Percent_Unchanged()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(128, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn * 4)));

        MidiSong same = MidiTransforms.ScaleTempo(song, 100);

        Assert.Equal(128.0, same.InitialBeatsPerMinute, precision: 1);
        Assert.Equal(song.Duration.TotalMilliseconds, same.Duration.TotalMilliseconds, precision: 0);
    }

    [Fact]
    public void ScaleTempo_ScalesLyricTimesAndMultipleTempoChanges()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[]
            {
                TestMidiBuilder.TempoBpm(120, 0),
                TestMidiBuilder.TempoBpm(60, Tpqn * 4),
            })
            .AddTrack(new[] { TestMidiBuilder.Lyric("a", Tpqn * 4) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn * 8)));

        MidiSong fast = MidiTransforms.ScaleTempo(song, 200);

        // Both tempo changes double in BPM.
        Assert.Equal(240.0, fast.TempoChanges[0].BeatsPerMinute, precision: 1);
        Assert.Equal(120.0, fast.TempoChanges[1].BeatsPerMinute, precision: 1);
        // Lyric at tick 1920 was 2000 ms in; now half that.
        Assert.Equal(1000, fast.Lyrics.Single().Time.TotalMilliseconds, precision: 0);
    }

    [Fact]
    public void ScaleTempo_NonPositive_Throws()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        Assert.Throws<ArgumentOutOfRangeException>(() => MidiTransforms.ScaleTempo(song, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MidiTransforms.ScaleTempo(song, -50));
    }

    // --- Tempo set BPM ---

    [Fact]
    public void SetBeatsPerMinute_SetsInitialTempo()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn * 4)));

        MidiSong retempo = MidiTransforms.SetBeatsPerMinute(song, 90);

        Assert.Equal(90.0, retempo.InitialBeatsPerMinute, precision: 1);
        // 120 -> 90 BPM is 0.75x speed => 2000 ms becomes ~2667 ms.
        Assert.Equal(2000.0 * 120 / 90, retempo.Duration.TotalMilliseconds, precision: 0);
    }

    [Fact]
    public void SetBeatsPerMinute_PreservesTempoMapProportions()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[]
            {
                TestMidiBuilder.TempoBpm(120, 0),
                TestMidiBuilder.TempoBpm(60, Tpqn * 4), // half the initial tempo
            })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn * 8)));

        MidiSong retempo = MidiTransforms.SetBeatsPerMinute(song, 60);

        // Initial halved to 60; the later change keeps its 0.5 ratio => 30 BPM.
        Assert.Equal(60.0, retempo.TempoChanges[0].BeatsPerMinute, precision: 1);
        Assert.Equal(30.0, retempo.TempoChanges[1].BeatsPerMinute, precision: 1);
    }

    [Fact]
    public void SetBeatsPerMinute_NonPositive_Throws()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        Assert.Throws<ArgumentOutOfRangeException>(() => MidiTransforms.SetBeatsPerMinute(song, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MidiTransforms.SetBeatsPerMinute(song, -10));
    }

    // --- MuteTrack / UnmuteTrack (guide melody) ---

    [Fact]
    public void MuteTrack_SetsVelocityToZero()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn, velocity: 100))
            .AddTrack(TestMidiBuilder.Note(64, 1, 0, Tpqn, velocity: 80)));

        MidiSong muted = MidiTransforms.MuteTrack(song, 1);

        Assert.All(muted.Tracks[1].Notes, n => Assert.Equal(0, n.Velocity));
        Assert.All(muted.Tracks[0].Notes, n => Assert.True(n.Velocity > 0));
    }

    [Fact]
    public void MuteTrack_OutOfRange_ReturnsUnchanged()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        Assert.Same(song, MidiTransforms.MuteTrack(song, -1));
        Assert.Same(song, MidiTransforms.MuteTrack(song, 99));
    }

    [Fact]
    public void UnmuteTrack_RestoresOriginalVelocity()
    {
        MidiSong original = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn, velocity: 100))
            .AddTrack(TestMidiBuilder.Note(64, 1, 0, Tpqn, velocity: 80)));

        MidiSong muted = MidiTransforms.MuteTrack(original, 1);
        Assert.Equal(0, muted.Tracks[1].Notes[0].Velocity);

        MidiSong restored = MidiTransforms.UnmuteTrack(muted, original, 1);
        Assert.Equal(80, restored.Tracks[1].Notes[0].Velocity);
    }

    [Fact]
    public void MuteTrack_PreservesNoteStructure()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn, velocity: 100)
                .Concat(TestMidiBuilder.Note(64, 0, Tpqn, Tpqn, velocity: 90))));

        MidiSong muted = MidiTransforms.MuteTrack(song, 0);
        Assert.Equal(song.Tracks[0].Notes.Count, muted.Tracks[0].Notes.Count);
        Assert.Equal(60, muted.Tracks[0].Notes[0].NoteNumber);
        Assert.Equal(64, muted.Tracks[0].Notes[1].NoteNumber);
    }
}
