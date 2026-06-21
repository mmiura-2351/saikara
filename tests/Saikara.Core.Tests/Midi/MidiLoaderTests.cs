using Melanchall.DryWetMidi.Interaction;
using Saikara.Core.Midi;

namespace Saikara.Core.Tests.Midi;

public class MidiLoaderTests
{
    private const short Tpqn = 480;

    [Fact]
    public void Load_ReadsTimeDivision()
    {
        var builder = new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn));

        MidiSong song = new MidiLoader().Load(builder.BuildStream());

        Assert.Equal(Tpqn, song.TicksPerQuarterNote);
    }

    [Fact]
    public void Load_ReadsNotesWithTickAndMetricTiming()
    {
        // 120 BPM => one quarter note (480 ticks) == 500 ms.
        var builder = new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(67, 0, Tpqn, Tpqn * 2)); // start at beat 1, 2 beats long

        MidiSong song = new MidiLoader().Load(builder.BuildStream());

        // The tempo track has no notes; the note lives on the second track.
        MidiNote note = Assert.Single(song.Tracks.SelectMany(t => t.Notes));
        Assert.Equal(67, note.NoteNumber);
        Assert.Equal(0, note.Channel);
        Assert.Equal(100, note.Velocity);

        Assert.Equal(Tpqn, note.StartTicks);
        Assert.Equal(Tpqn * 2, note.LengthTicks);
        Assert.Equal(Tpqn * 3, note.EndTicks);

        Assert.Equal(500, note.StartTime.TotalMilliseconds, precision: 0);
        Assert.Equal(1000, note.Length.TotalMilliseconds, precision: 0);
        Assert.Equal(1500, note.EndTime.TotalMilliseconds, precision: 0);
    }

    [Fact]
    public void Load_ReadsInitialTempoAsBpmAndMicroseconds()
    {
        var builder = new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(140, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn));

        MidiSong song = new MidiLoader().Load(builder.BuildStream());

        Assert.Equal(140.0, song.InitialBeatsPerMinute, precision: 1);
        TempoChange initial = song.InitialTempo;
        Assert.Equal(0, initial.TimeTicks);
        Assert.Equal(TimeSpan.Zero, initial.Time);
        // 140 BPM == 60_000_000 / 140 micros per quarter.
        Assert.Equal((long)Math.Round(60_000_000.0 / 140), initial.MicrosecondsPerQuarterNote);
    }

    [Fact]
    public void Load_WithoutTempoEvent_DefaultsTo120Bpm()
    {
        var builder = new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn));

        MidiSong song = new MidiLoader().Load(builder.BuildStream());

        Assert.Equal(120.0, song.InitialBeatsPerMinute, precision: 1);
        Assert.Single(song.TempoChanges);
    }

    [Fact]
    public void Load_ReadsTempoMapWithMultipleChanges()
    {
        var builder = new TestMidiBuilder(Tpqn)
            .AddTrack(new[]
            {
                TestMidiBuilder.TempoBpm(120, 0),
                TestMidiBuilder.TempoBpm(60, Tpqn * 4),
            })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn * 8));

        MidiSong song = new MidiLoader().Load(builder.BuildStream());

        Assert.Equal(2, song.TempoChanges.Count);
        Assert.Equal(120.0, song.TempoChanges[0].BeatsPerMinute, precision: 1);
        Assert.Equal(0, song.TempoChanges[0].TimeTicks);

        Assert.Equal(60.0, song.TempoChanges[1].BeatsPerMinute, precision: 1);
        Assert.Equal(Tpqn * 4, song.TempoChanges[1].TimeTicks);
        // 4 quarters at 120 BPM == 2000 ms before the change.
        Assert.Equal(2000, song.TempoChanges[1].Time.TotalMilliseconds, precision: 0);
    }

    [Fact]
    public void Load_ExtractsLyricAndTextEvents()
    {
        var builder = new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(new[]
            {
                TestMidiBuilder.Lyric("Hel", Tpqn),
                TestMidiBuilder.Lyric("lo", Tpqn + Tpqn / 2),
                TestMidiBuilder.Text("[title]", 0),
            });

        MidiSong song = new MidiLoader().Load(builder.BuildStream());

        Assert.Equal(3, song.Lyrics.Count);
        // Ordered by time: the text at tick 0 comes first.
        Assert.Equal("[title]", song.Lyrics[0].Text);
        Assert.False(song.Lyrics[0].IsLyric);

        Assert.Equal("Hel", song.Lyrics[1].Text);
        Assert.True(song.Lyrics[1].IsLyric);
        Assert.Equal(Tpqn, song.Lyrics[1].TimeTicks);
        Assert.Equal(500, song.Lyrics[1].Time.TotalMilliseconds, precision: 0);

        Assert.Equal("lo", song.Lyrics[2].Text);
    }

    [Fact]
    public void Load_CollectsChannelsAndTrackNames()
    {
        var builder = new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TrackName("Conductor") }) // named, no notes
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)
                .Concat(TestMidiBuilder.Note(64, 3, Tpqn, Tpqn))
                .Prepend(TestMidiBuilder.TrackName("Piano")));

        MidiSong song = new MidiLoader().Load(builder.BuildStream());

        Assert.Equal(2, song.Tracks.Count);

        Assert.Equal("Conductor", song.Tracks[0].Name);
        Assert.Empty(song.Tracks[0].Notes);
        Assert.Empty(song.Tracks[0].Channels);

        Assert.Equal("Piano", song.Tracks[1].Name);
        Assert.Equal(2, song.Tracks[1].Notes.Count);
        Assert.Equal(new[] { 0, 3 }, song.Tracks[1].Channels);
    }

    [Fact]
    public void Load_NotesAreOrderedByStartTick()
    {
        var builder = new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, Tpqn * 2, Tpqn)
                .Concat(TestMidiBuilder.Note(62, 0, 0, Tpqn))
                .Concat(TestMidiBuilder.Note(64, 0, Tpqn, Tpqn)));

        MidiSong song = new MidiLoader().Load(builder.BuildStream());

        var starts = song.Tracks[0].Notes.Select(n => n.StartTicks).ToList();
        Assert.Equal(new long[] { 0, Tpqn, Tpqn * 2 }, starts);
    }

    [Fact]
    public void Load_ComputesDuration()
    {
        // 120 BPM; last note ends at tick 480*4 == 2000 ms.
        var builder = new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn * 4));

        MidiSong song = new MidiLoader().Load(builder.BuildStream());

        Assert.Equal(2000, song.Duration.TotalMilliseconds, precision: 0);
    }

    [Fact]
    public void Load_FromFilePath_RoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"saikara-midi-{Guid.NewGuid():N}.mid");
        try
        {
            new TestMidiBuilder(Tpqn)
                .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
                .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn))
                .WriteToFile(path);

            MidiSong song = new MidiLoader().Load(path);

            Assert.Equal(Tpqn, song.TicksPerQuarterNote);
            Assert.Equal(60, song.Tracks.SelectMany(t => t.Notes).Single().NoteNumber);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NullArguments_Throw()
    {
        var loader = new MidiLoader();
        Assert.Throws<ArgumentNullException>(() => loader.Load((string)null!));
        Assert.Throws<ArgumentNullException>(() => loader.Load((Stream)null!));
    }
}
