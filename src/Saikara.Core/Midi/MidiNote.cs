namespace Saikara.Core.Midi;

/// <summary>
/// A single note in a <see cref="MidiSong"/>. Timing is exposed in BOTH MIDI ticks
/// (resolution-dependent integers, used for transforms and round-tripping) and metric
/// time (<see cref="TimeSpan"/>, used for playback, scoring and lyric sync). The two
/// representations are linked by the song's tempo map.
/// </summary>
public sealed record MidiNote
{
    /// <summary>MIDI note number, 0-127 (60 == middle C / C4).</summary>
    public required int NoteNumber { get; init; }

    /// <summary>
    /// Zero-based MIDI channel, 0-15. Channel index 9 (MIDI channel 10) is the
    /// percussion channel and is never transposed by the key-change transform.
    /// </summary>
    public required int Channel { get; init; }

    /// <summary>On-velocity, 0-127. Useful for the guide melody and dynamics scoring.</summary>
    public required int Velocity { get; init; }

    /// <summary>Note start time in MIDI ticks from the start of the song.</summary>
    public required long StartTicks { get; init; }

    /// <summary>Note length in MIDI ticks.</summary>
    public required long LengthTicks { get; init; }

    /// <summary>Note start time as metric (wall-clock) time from the start of the song.</summary>
    public required TimeSpan StartTime { get; init; }

    /// <summary>Note length as metric (wall-clock) duration.</summary>
    public required TimeSpan Length { get; init; }

    /// <summary>Note end time in MIDI ticks (<see cref="StartTicks"/> + <see cref="LengthTicks"/>).</summary>
    public long EndTicks => StartTicks + LengthTicks;

    /// <summary>Note end time as metric (wall-clock) time (<see cref="StartTime"/> + <see cref="Length"/>).</summary>
    public TimeSpan EndTime => StartTime + Length;
}
