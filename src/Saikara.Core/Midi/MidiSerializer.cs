using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace Saikara.Core.Midi;

/// <summary>
/// Serializes a <see cref="MidiSong"/> back to a Standard MIDI File byte stream with
/// DryWetMIDI. This lets a transformed song — after
/// <see cref="MidiTransforms.Transpose(MidiSong, int)"/> or
/// <see cref="MidiTransforms.ScaleTempo(MidiSong, double)"/> — be re-emitted as a real SMF
/// and handed to the SoundFont synth in <c>Saikara.App</c>, which consumes MIDI bytes/streams
/// rather than the in-memory model. Pure managed and cross-platform.
/// </summary>
/// <remarks>
/// Reconstruction uses the tick-based fields of the model (<see cref="MidiNote.StartTicks"/>,
/// <see cref="MidiNote.LengthTicks"/>, <see cref="TempoChange.TimeTicks"/>,
/// <see cref="LyricEvent.TimeTicks"/>) as the source of truth, against the song's
/// <see cref="MidiSong.TicksPerQuarterNote"/> resolution. Metric (<see cref="TimeSpan"/>)
/// fields are derived from the tempo map when the result is reloaded, so the two
/// representations stay consistent across a round trip. The layout is a type-1 (multi-track)
/// SMF: a leading conductor track carrying the tempo map and all lyric/text events, followed
/// by one track chunk per <see cref="MidiTrack"/>.
/// </remarks>
public sealed class MidiSerializer
{
    /// <summary>Writes <paramref name="song"/> as a Standard MIDI File to <paramref name="stream"/>.</summary>
    /// <param name="song">The song to serialize. Not mutated.</param>
    /// <param name="stream">The destination stream. Not disposed by this method.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public void Write(MidiSong song, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(stream);

        MidiFile file = Build(song);
        file.Write(stream, MidiFileFormat.MultiTrack);
    }

    /// <summary>Serializes <paramref name="song"/> to a Standard MIDI File byte array.</summary>
    /// <param name="song">The song to serialize. Not mutated.</param>
    /// <returns>The SMF bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="song"/> is <see langword="null"/>.</exception>
    public byte[] ToBytes(MidiSong song)
    {
        ArgumentNullException.ThrowIfNull(song);

        using var stream = new MemoryStream();
        Write(song, stream);
        return stream.ToArray();
    }

    private static MidiFile Build(MidiSong song)
    {
        var chunks = new List<TrackChunk> { BuildConductorTrack(song) };
        chunks.AddRange(song.Tracks.Select(BuildTrack));

        return new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(song.TicksPerQuarterNote),
        };
    }

    /// <summary>
    /// Builds the leading conductor track holding the tempo map and the lyric/text stream.
    /// Keeping these on a single, note-free chunk mirrors how type-1 karaoke SMFs are laid
    /// out and keeps the tempo map authoritative for the whole file.
    /// </summary>
    private static TrackChunk BuildConductorTrack(MidiSong song)
    {
        var events = new List<TimedEvent>();

        foreach (TempoChange tempo in song.TempoChanges)
        {
            events.Add(new TimedEvent(
                new SetTempoEvent(tempo.MicrosecondsPerQuarterNote),
                tempo.TimeTicks));
        }

        foreach (LyricEvent lyric in song.Lyrics)
        {
            MidiEvent textEvent = lyric.IsLyric
                ? new Melanchall.DryWetMidi.Core.LyricEvent(lyric.Text)
                : new TextEvent(lyric.Text);
            events.Add(new TimedEvent(textEvent, lyric.TimeTicks));
        }

        return events.ToTrackChunk();
    }

    private static TrackChunk BuildTrack(MidiTrack track)
    {
        var events = new List<TimedEvent>();

        if (track.Name is { } name)
        {
            events.Add(new TimedEvent(new SequenceTrackNameEvent(name), 0));
        }

        foreach (MidiNote note in track.Notes)
        {
            var channel = (FourBitNumber)note.Channel;
            var noteNumber = (SevenBitNumber)note.NoteNumber;

            var on = new NoteOnEvent(noteNumber, (SevenBitNumber)note.Velocity) { Channel = channel };
            // Note-off as a zero-velocity event keeps notes paired the way the loader expects.
            var off = new NoteOffEvent(noteNumber, (SevenBitNumber)0) { Channel = channel };

            events.Add(new TimedEvent(on, note.StartTicks));
            events.Add(new TimedEvent(off, note.EndTicks));
        }

        return events.ToTrackChunk();
    }
}
