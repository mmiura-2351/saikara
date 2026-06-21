using Melanchall.DryWetMidi.Interaction;
using Saikara.Core.Midi;
using Saikara.Core.Scoring;
using Saikara.Core.Tests.Midi;

namespace Saikara.Core.Tests.Scoring;

public class ReferenceMelodyTests
{
    private static MidiSong LoadTwoTrackSong()
    {
        // Track 0: a named "Melody" track with three ascending notes.
        // Track 1: an accompaniment track.
        var builder = new TestMidiBuilder(ticksPerQuarterNote: 480);

        var melodyEvents = new List<TimedEvent> { TestMidiBuilder.TrackName("Melody") };
        melodyEvents.AddRange(TestMidiBuilder.Note(noteNumber: 60, channel: 0, startTicks: 0, lengthTicks: 480));
        melodyEvents.AddRange(TestMidiBuilder.Note(noteNumber: 62, channel: 0, startTicks: 480, lengthTicks: 480));
        melodyEvents.AddRange(TestMidiBuilder.Note(noteNumber: 64, channel: 0, startTicks: 960, lengthTicks: 480));
        builder.AddTrack(melodyEvents);

        var accompEvents = new List<TimedEvent> { TestMidiBuilder.TrackName("Piano") };
        accompEvents.AddRange(TestMidiBuilder.Note(noteNumber: 48, channel: 1, startTicks: 0, lengthTicks: 1440));
        builder.AddTrack(accompEvents);

        using MemoryStream stream = builder.BuildStream();
        return new MidiLoader().Load(stream);
    }

    [Fact]
    public void FromTrack_ReturnsNotesInStartOrder_WithCorrectPitchesAndTimes()
    {
        MidiSong song = LoadTwoTrackSong();
        int melodyIndex = MelodyTrackDetector.Detect(song) ?? throw new Xunit.Sdk.XunitException("melody not detected");

        IReadOnlyList<ReferenceNote> notes = ReferenceMelody.FromTrack(song, melodyIndex, semitoneOffset: 0);

        Assert.Equal(3, notes.Count);
        Assert.Equal(new[] { 60, 62, 64 }, notes.Select(n => n.MidiNote).ToArray());

        // Times are non-decreasing and durations are positive.
        for (int i = 1; i < notes.Count; i++)
        {
            Assert.True(notes[i].Start >= notes[i - 1].Start);
        }

        foreach (ReferenceNote n in notes)
        {
            Assert.True(n.Duration > TimeSpan.Zero);
        }
    }

    [Fact]
    public void FromTrack_AppliesSemitoneOffset()
    {
        MidiSong song = LoadTwoTrackSong();
        int melodyIndex = MelodyTrackDetector.Detect(song)!.Value;

        IReadOnlyList<ReferenceNote> up = ReferenceMelody.FromTrack(song, melodyIndex, semitoneOffset: 2);

        Assert.Equal(new[] { 62, 64, 66 }, up.Select(n => n.MidiNote).ToArray());
    }

    [Fact]
    public void FromTrack_ClampsTransposeToValidRange()
    {
        MidiSong song = LoadTwoTrackSong();
        int melodyIndex = MelodyTrackDetector.Detect(song)!.Value;

        IReadOnlyList<ReferenceNote> down = ReferenceMelody.FromTrack(song, melodyIndex, semitoneOffset: -200);

        Assert.All(down, n => Assert.InRange(n.MidiNote, 0, 127));
        Assert.All(down, n => Assert.Equal(0, n.MidiNote));
    }

    [Fact]
    public void FromTrack_NullSong_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ReferenceMelody.FromTrack(null!, 0));
    }

    [Fact]
    public void FromTrack_BadTrackIndex_Throws()
    {
        MidiSong song = LoadTwoTrackSong();
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceMelody.FromTrack(song, 99));
    }

    [Fact]
    public void ReferenceNote_Create_RejectsNegativeDurationAndBadPitch()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReferenceNote.Create(TimeSpan.Zero, TimeSpan.FromSeconds(-1), 60));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReferenceNote.Create(TimeSpan.Zero, TimeSpan.FromSeconds(1), 200));
    }

    [Fact]
    public void PitchSample_VoicedAt_RejectsNonPositiveFrequency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PitchSample.VoicedAt(TimeSpan.Zero, 0.0));
    }
}
