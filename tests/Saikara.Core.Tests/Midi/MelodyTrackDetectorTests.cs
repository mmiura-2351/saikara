using Saikara.Core.Midi;

namespace Saikara.Core.Tests.Midi;

public class MelodyTrackDetectorTests
{
    private const short Tpqn = 480;

    private static MidiSong Load(TestMidiBuilder builder)
        => new MidiLoader().Load(builder.BuildStream());

    [Fact]
    public void Detect_FindsTrackNamedMelody()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(48, 0, 0, Tpqn).Prepend(TestMidiBuilder.TrackName("Bass")))
            .AddTrack(TestMidiBuilder.Note(72, 0, 0, Tpqn).Prepend(TestMidiBuilder.TrackName("Melody"))));

        Assert.Equal(1, MelodyTrackDetector.Detect(song));
    }

    [Fact]
    public void Detect_IsCaseInsensitiveAndSubstring()
    {
        // Real karaoke SMFs are type-1 with a leading conductor track, so model two tracks.
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(48, 0, 0, Tpqn).Prepend(TestMidiBuilder.TrackName("Bass")))
            .AddTrack(TestMidiBuilder.Note(72, 0, 0, Tpqn).Prepend(TestMidiBuilder.TrackName("Lead MELODY gtr"))));

        Assert.Equal(1, MelodyTrackDetector.Detect(song));
    }

    [Fact]
    public void Detect_ReturnsNullWhenNoMelodyName()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(48, 0, 0, Tpqn).Prepend(TestMidiBuilder.TrackName("Bass")))
            .AddTrack(TestMidiBuilder.Note(72, 0, 0, Tpqn).Prepend(TestMidiBuilder.TrackName("Piano"))));

        Assert.Null(MelodyTrackDetector.Detect(song));
    }

    [Fact]
    public void Detect_ReturnsNullWhenTracksAreUnnamed()
    {
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(72, 0, 0, Tpqn)));

        Assert.Null(MelodyTrackDetector.Detect(song));
    }

    [Fact]
    public void Detect_IgnoresEmptyMelodyNamedTrack()
    {
        // A track named "Melody" but with no notes is not a usable reference.
        MidiSong song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[] { TestMidiBuilder.TrackName("Melody") })
            .AddTrack(TestMidiBuilder.Note(72, 0, 0, Tpqn).Prepend(TestMidiBuilder.TrackName("Guide Melody"))));

        Assert.Equal(1, MelodyTrackDetector.Detect(song));
    }
}
