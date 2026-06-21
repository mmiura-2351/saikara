using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace Saikara.Core.Tests.Midi;

/// <summary>
/// Builds small in-memory Standard MIDI Files with DryWetMIDI so the loader tests do not
/// depend on external fixture files. Times are given in absolute ticks; the builder turns
/// them into the delta-timed track chunks an SMF actually stores.
/// </summary>
/// <remarks>
/// Note: a file with a <b>single</b> track that carries both a name and notes is split by
/// the writer into a name-only conductor chunk plus a notes chunk on round-trip (mirroring
/// how real type-1 SMFs are laid out). Build multi-track files (as real karaoke SMFs are)
/// when a named track must keep its notes.
/// </remarks>
internal sealed class TestMidiBuilder
{
    private readonly short _ticksPerQuarterNote;
    private readonly List<TrackChunk> _trackChunks = new();

    public TestMidiBuilder(short ticksPerQuarterNote = 480)
    {
        _ticksPerQuarterNote = ticksPerQuarterNote;
    }

    /// <summary>Adds a track built from the given timed events (absolute tick times).</summary>
    public TestMidiBuilder AddTrack(IEnumerable<TimedEvent> events)
    {
        _trackChunks.Add(events.ToTrackChunk());
        return this;
    }

    /// <summary>Materializes the file as an in-memory SMF stream positioned at the start.</summary>
    public MemoryStream BuildStream()
    {
        var file = BuildFile();
        var stream = new MemoryStream();
        file.Write(stream, MidiFileFormat.MultiTrack);
        stream.Position = 0;
        return stream;
    }

    /// <summary>Writes the file to <paramref name="path"/> on disk.</summary>
    public void WriteToFile(string path)
    {
        BuildFile().Write(path, overwriteFile: true, MidiFileFormat.MultiTrack);
    }

    private MidiFile BuildFile()
    {
        var file = new MidiFile(_trackChunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(_ticksPerQuarterNote),
        };
        return file;
    }

    // --- Event factories (absolute-time TimedEvents) ---

    public static TimedEvent TrackName(string name)
        => new(new SequenceTrackNameEvent(name), 0);

    public static TimedEvent Tempo(long microsecondsPerQuarterNote, long time)
        => new(new SetTempoEvent(microsecondsPerQuarterNote), time);

    public static TimedEvent TempoBpm(double bpm, long time)
        => new(new SetTempoEvent((long)Math.Round(60_000_000.0 / bpm)), time);

    public static TimedEvent Lyric(string text, long time)
        => new(new LyricEvent(text), time);

    public static TimedEvent Text(string text, long time)
        => new(new TextEvent(text), time);

    /// <summary>Returns the note-on and note-off pair for a single note at absolute ticks.</summary>
    public static IEnumerable<TimedEvent> Note(
        int noteNumber, int channel, long startTicks, long lengthTicks, int velocity = 100)
    {
        var on = new NoteOnEvent((SevenBitNumber)noteNumber, (SevenBitNumber)velocity)
        {
            Channel = (FourBitNumber)channel,
        };
        var off = new NoteOffEvent((SevenBitNumber)noteNumber, (SevenBitNumber)0)
        {
            Channel = (FourBitNumber)channel,
        };
        yield return new TimedEvent(on, startTicks);
        yield return new TimedEvent(off, startTicks + lengthTicks);
    }
}
