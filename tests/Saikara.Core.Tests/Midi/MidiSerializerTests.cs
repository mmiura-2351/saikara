using Saikara.Core.Midi;

namespace Saikara.Core.Tests.Midi;

public class MidiSerializerTests
{
    private const short Tpqn = 480;

    private static MidiSong Load(TestMidiBuilder builder)
        => new MidiLoader().Load(builder.BuildStream());

    private static MidiSong RoundTrip(MidiSong song)
    {
        byte[] bytes = new MidiSerializer().ToBytes(song);
        using var stream = new MemoryStream(bytes);
        return new MidiLoader().Load(stream);
    }

    [Fact]
    public void ToBytes_ProducesReadableSmf()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        byte[] bytes = new MidiSerializer().ToBytes(song);

        Assert.NotEmpty(bytes);
        // Standard MIDI File header chunk identifier.
        Assert.Equal((byte)'M', bytes[0]);
        Assert.Equal((byte)'T', bytes[1]);
        Assert.Equal((byte)'h', bytes[2]);
        Assert.Equal((byte)'d', bytes[3]);
    }

    [Fact]
    public void RoundTrip_PreservesTimeDivision()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        MidiSong reloaded = RoundTrip(song);

        Assert.Equal(Tpqn, reloaded.TicksPerQuarterNote);
    }

    [Fact]
    public void RoundTrip_PreservesNotes()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn, velocity: 90)
                .Concat(TestMidiBuilder.Note(64, 3, Tpqn, Tpqn * 2, velocity: 110))
                .Concat(TestMidiBuilder.Note(38, 9, Tpqn * 2, Tpqn, velocity: 80))));

        MidiSong reloaded = RoundTrip(song);

        var original = song.Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTicks).ThenBy(n => n.NoteNumber).ToList();
        var actual = reloaded.Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTicks).ThenBy(n => n.NoteNumber).ToList();

        Assert.Equal(original.Count, actual.Count);
        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].NoteNumber, actual[i].NoteNumber);
            Assert.Equal(original[i].Channel, actual[i].Channel);
            Assert.Equal(original[i].Velocity, actual[i].Velocity);
            Assert.Equal(original[i].StartTicks, actual[i].StartTicks);
            Assert.Equal(original[i].LengthTicks, actual[i].LengthTicks);
        }
    }

    [Fact]
    public void RoundTrip_PreservesNoteMetricTiming()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(67, 0, Tpqn, Tpqn * 2)));

        MidiSong reloaded = RoundTrip(song);

        MidiNote note = Assert.Single(reloaded.Tracks.SelectMany(t => t.Notes));
        Assert.Equal(500, note.StartTime.TotalMilliseconds, precision: 0);
        Assert.Equal(1000, note.Length.TotalMilliseconds, precision: 0);
    }

    [Fact]
    public void RoundTrip_PreservesTempoMap()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[]
            {
                TestMidiBuilder.TempoBpm(140, 0),
                TestMidiBuilder.TempoBpm(70, Tpqn * 4),
            })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn * 8)));

        MidiSong reloaded = RoundTrip(song);

        Assert.Equal(2, reloaded.TempoChanges.Count);
        Assert.Equal(140.0, reloaded.TempoChanges[0].BeatsPerMinute, precision: 1);
        Assert.Equal(0, reloaded.TempoChanges[0].TimeTicks);
        Assert.Equal(70.0, reloaded.TempoChanges[1].BeatsPerMinute, precision: 1);
        Assert.Equal(Tpqn * 4, reloaded.TempoChanges[1].TimeTicks);
    }

    [Fact]
    public void RoundTrip_PreservesLyricsAndText()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(new[]
            {
                TestMidiBuilder.Text("[title]", 0),
                TestMidiBuilder.Lyric("Hel", Tpqn),
                TestMidiBuilder.Lyric("lo", Tpqn + Tpqn / 2),
            })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn * 4)));

        MidiSong reloaded = RoundTrip(song);

        Assert.Equal(3, reloaded.Lyrics.Count);
        Assert.Equal("[title]", reloaded.Lyrics[0].Text);
        Assert.False(reloaded.Lyrics[0].IsLyric);

        Assert.Equal("Hel", reloaded.Lyrics[1].Text);
        Assert.True(reloaded.Lyrics[1].IsLyric);
        Assert.Equal(Tpqn, reloaded.Lyrics[1].TimeTicks);

        Assert.Equal("lo", reloaded.Lyrics[2].Text);
        Assert.True(reloaded.Lyrics[2].IsLyric);
    }

    [Fact]
    public void RoundTrip_PreservesTrackNames()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)
                .Prepend(TestMidiBuilder.TrackName("Melody"))));

        MidiSong reloaded = RoundTrip(song);

        Assert.Contains(reloaded.Tracks, t => t.Name == "Melody");
    }

    [Fact]
    public void Serializing_TransposedSong_ReflectsShiftedNotes()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)
                .Concat(TestMidiBuilder.Note(64, 0, Tpqn, Tpqn))
                .Concat(TestMidiBuilder.Note(38, 9, Tpqn * 2, Tpqn)))); // percussion

        MidiSong transposed = MidiTransforms.Transpose(song, 5);
        MidiSong reloaded = RoundTrip(transposed);

        var byChannel = reloaded.Tracks.SelectMany(t => t.Notes).ToList();
        var melodic = byChannel.Where(n => n.Channel == 0)
            .OrderBy(n => n.StartTicks).Select(n => n.NoteNumber).ToList();
        Assert.Equal(new[] { 65, 69 }, melodic);

        // Percussion (channel index 9) is never transposed.
        MidiNote drum = Assert.Single(byChannel.Where(n => n.Channel == 9));
        Assert.Equal(38, drum.NoteNumber);
    }

    [Fact]
    public void Serializing_ScaledTempoSong_ReflectsNewTempo()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn * 4)));

        MidiSong fast = MidiTransforms.ScaleTempo(song, 150);
        MidiSong reloaded = RoundTrip(fast);

        Assert.Equal(180.0, reloaded.InitialBeatsPerMinute, precision: 1);
    }

    [Fact]
    public void Write_ToStream_MatchesToBytes()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TempoBpm(120, 0) })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        var serializer = new MidiSerializer();
        byte[] viaBytes = serializer.ToBytes(song);

        using var stream = new MemoryStream();
        serializer.Write(song, stream);

        Assert.Equal(viaBytes, stream.ToArray());
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var serializer = new MidiSerializer();
        Assert.Throws<ArgumentNullException>(() => serializer.ToBytes(null!));
        Assert.Throws<ArgumentNullException>(() => serializer.Write(null!, new MemoryStream()));

        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));
        Assert.Throws<ArgumentNullException>(() => serializer.Write(song, null!));
    }
}
