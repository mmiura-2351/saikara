using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace Saikara.Core.Midi;

/// <summary>
/// DryWetMIDI-backed implementation of <see cref="IMidiLoader"/>. Reads a Standard MIDI File
/// (incl. <c>.kar</c> / Yamaha XF) and projects it onto the platform-agnostic
/// <see cref="MidiSong"/> model: tracks and notes (timed in both ticks and metric time),
/// the tempo map, and the lyric/text stream. Pure managed and cross-platform.
/// </summary>
public sealed class MidiLoader : IMidiLoader
{
    /// <inheritdoc />
    public MidiSong Load(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var file = MidiFile.Read(filePath);
        return Build(file);
    }

    /// <inheritdoc />
    public MidiSong Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var file = MidiFile.Read(stream);
        return Build(file);
    }

    private static MidiSong Build(MidiFile file)
    {
        if (file.TimeDivision is not TicksPerQuarterNoteTimeDivision division)
        {
            throw new NotSupportedException(
                "Only tick-based (metrical) MIDI time division is supported; " +
                "SMPTE time-code division is not used by karaoke files.");
        }

        var tempoMap = file.GetTempoMap();
        var tempoChanges = BuildTempoChanges(tempoMap);
        var tracks = BuildTracks(file, tempoMap);
        var lyrics = BuildLyrics(file, tempoMap);
        TimeSpan duration = file.GetDuration<MetricTimeSpan>();

        return new MidiSong
        {
            TicksPerQuarterNote = division.TicksPerQuarterNote,
            Duration = duration,
            TempoChanges = tempoChanges,
            Tracks = tracks,
            Lyrics = lyrics,
        };
    }

    private static IReadOnlyList<TempoChange> BuildTempoChanges(TempoMap tempoMap)
    {
        var changes = new List<TempoChange>();

        // GetTempoChanges() omits the implicit tempo at time zero, so seed it explicitly
        // with whatever tempo is in effect there (the file's tempo, or DryWetMIDI's
        // default of 120 BPM when the file sets none).
        var initial = tempoMap.GetTempoAtTime((MidiTimeSpan)0L);
        changes.Add(new TempoChange
        {
            TimeTicks = 0,
            Time = TimeSpan.Zero,
            MicrosecondsPerQuarterNote = initial.MicrosecondsPerQuarterNote,
        });

        foreach (var change in tempoMap.GetTempoChanges())
        {
            long ticks = change.Time;
            if (ticks == 0)
            {
                // Replace the seeded entry rather than duplicate time zero.
                changes[0] = changes[0] with
                {
                    MicrosecondsPerQuarterNote = change.Value.MicrosecondsPerQuarterNote,
                };
                continue;
            }

            changes.Add(new TempoChange
            {
                TimeTicks = ticks,
                Time = TicksToTime(ticks, tempoMap),
                MicrosecondsPerQuarterNote = change.Value.MicrosecondsPerQuarterNote,
            });
        }

        return changes;
    }

    private static IReadOnlyList<MidiTrack> BuildTracks(MidiFile file, TempoMap tempoMap)
    {
        var tracks = new List<MidiTrack>();

        foreach (var trackChunk in file.GetTrackChunks())
        {
            string? name = trackChunk.Events
                .OfType<SequenceTrackNameEvent>()
                .FirstOrDefault()?.Text;

            var notes = trackChunk
                .GetNotes()
                .Select(n => ToModelNote(n, tempoMap))
                .OrderBy(n => n.StartTicks)
                .ToList();

            var channels = notes
                .Select(n => n.Channel)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            tracks.Add(new MidiTrack
            {
                Name = name,
                Channels = channels,
                Notes = notes,
            });
        }

        return tracks;
    }

    private static MidiNote ToModelNote(Note note, TempoMap tempoMap)
    {
        long startTicks = note.Time;
        long lengthTicks = note.Length;

        TimeSpan startTime = TicksToTime(startTicks, tempoMap);
        TimeSpan length = LengthConverter.ConvertTo<MetricTimeSpan>(lengthTicks, startTicks, tempoMap);

        return new MidiNote
        {
            NoteNumber = note.NoteNumber,
            Channel = note.Channel,
            Velocity = note.Velocity,
            StartTicks = startTicks,
            LengthTicks = lengthTicks,
            StartTime = startTime,
            Length = length,
        };
    }

    private static IReadOnlyList<LyricEvent> BuildLyrics(MidiFile file, TempoMap tempoMap)
    {
        var lyrics = new List<LyricEvent>();

        foreach (var timedEvent in file.GetTimedEvents())
        {
            bool isLyric = timedEvent.Event is Melanchall.DryWetMidi.Core.LyricEvent;
            bool isText = timedEvent.Event is TextEvent;
            if (!isLyric && !isText)
            {
                continue;
            }

            long ticks = timedEvent.Time;
            string text = ((BaseTextEvent)timedEvent.Event).Text ?? string.Empty;

            lyrics.Add(new LyricEvent
            {
                TimeTicks = ticks,
                Time = TicksToTime(ticks, tempoMap),
                Text = text,
                IsLyric = isLyric,
            });
        }

        return lyrics
            .OrderBy(l => l.TimeTicks)
            .ToList();
    }

    /// <summary>Converts an absolute tick to metric (wall-clock) time using the tempo map.</summary>
    private static TimeSpan TicksToTime(long ticks, TempoMap tempoMap)
        => TimeConverter.ConvertTo<MetricTimeSpan>(ticks, tempoMap);
}
