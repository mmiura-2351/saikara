using Saikara.Core.Import;
using Saikara.Core.Midi;
using Saikara.Core.Tests.Midi;

namespace Saikara.Core.Tests.Import;

public class KaraokeMetadataExtractorTests
{
    private const short Tpqn = 480;

    private static MidiSong Load(TestMidiBuilder builder)
        => new MidiLoader().Load(builder.BuildStream());

    [Fact]
    public void Extract_ReadsTitleAndArtist_FromAtTags()
    {
        var song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[]
            {
                TestMidiBuilder.Text("@KMIDI KARAOKE FILE", 0),
                TestMidiBuilder.Text("@T Sakura", 0),
                TestMidiBuilder.Text("@A Naotaro Moriyama", 0),
            })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        KaraokeMetadata meta = KaraokeMetadataExtractor.Extract(song);

        Assert.Equal("Sakura", meta.Title);
        Assert.Equal("Naotaro Moriyama", meta.Artist);
    }

    [Fact]
    public void Extract_UsesFirstTitleTag_WhenMultiplePresent()
    {
        var song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[]
            {
                TestMidiBuilder.Text("@T Real Title", 0),
                TestMidiBuilder.Text("@T Album Info", 0),
            })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        Assert.Equal("Real Title", KaraokeMetadataExtractor.Extract(song).Title);
    }

    [Fact]
    public void Extract_FallsBackToInfoTag_ForArtist_WhenNoAuthorTag()
    {
        var song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[]
            {
                TestMidiBuilder.Text("@T Title Only", 0),
                TestMidiBuilder.Text("@I Some Band", 0),
            })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        Assert.Equal("Some Band", KaraokeMetadataExtractor.Extract(song).Artist);
    }

    [Fact]
    public void Extract_PrefersAuthorTagOverInfoTag()
    {
        var song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(new[]
            {
                TestMidiBuilder.Text("@A The Author", 0),
                TestMidiBuilder.Text("@I The Info", 0),
            })
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        Assert.Equal("The Author", KaraokeMetadataExtractor.Extract(song).Artist);
    }

    [Fact]
    public void Extract_FallsBackToTrackName_WhenNoTitleTag()
    {
        var song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)
                .Prepend(TestMidiBuilder.TrackName("My Song Name"))));

        Assert.Equal("My Song Name", KaraokeMetadataExtractor.Extract(song).Title);
    }

    [Fact]
    public void Extract_ReturnsNulls_WhenNoMetadataPresent()
    {
        var song = Load(new TestMidiBuilder(Tpqn)
            .AddTrack(TestMidiBuilder.Note(60, 0, 0, Tpqn)));

        KaraokeMetadata meta = KaraokeMetadataExtractor.Extract(song);
        Assert.Null(meta.Title);
        Assert.Null(meta.Artist);
    }

    [Fact]
    public void Extract_NullSong_Throws()
        => Assert.Throws<ArgumentNullException>(() => KaraokeMetadataExtractor.Extract(null!));
}
