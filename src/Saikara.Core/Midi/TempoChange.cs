namespace Saikara.Core.Midi;

/// <summary>
/// A single tempo event in the song's tempo map: the tempo that takes effect at a given
/// time. The first change is always present and starts at tick 0 / time zero.
/// </summary>
public sealed record TempoChange
{
    /// <summary>Tick at which this tempo takes effect.</summary>
    public required long TimeTicks { get; init; }

    /// <summary>Metric (wall-clock) time at which this tempo takes effect.</summary>
    public required TimeSpan Time { get; init; }

    /// <summary>Tempo expressed in microseconds per quarter note (the native SMF unit).</summary>
    public required long MicrosecondsPerQuarterNote { get; init; }

    /// <summary>Tempo expressed in beats (quarter notes) per minute.</summary>
    public double BeatsPerMinute => 60_000_000.0 / MicrosecondsPerQuarterNote;
}
