namespace Saikara.Core.Scoring;

/// <summary>
/// A single note of the reference melody the singer is scored against: the target MIDI pitch
/// over a metric (wall-clock) time window <c>[<see cref="Start"/>, <see cref="End"/>)</c>.
/// Built from the designated melody track via <see cref="ReferenceMelody.FromTrack"/>.
/// </summary>
public readonly record struct ReferenceNote
{
    /// <summary>Note start time from the start of the song.</summary>
    public TimeSpan Start { get; init; }

    /// <summary>Note end time from the start of the song; must not precede <see cref="Start"/>.</summary>
    public TimeSpan End { get; init; }

    /// <summary>The target MIDI note number, 0-127 (60 == middle C / C4).</summary>
    public int MidiNote { get; init; }

    /// <summary>The note's duration (<see cref="End"/> minus <see cref="Start"/>).</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>
    /// Creates a reference note from a start time, duration and target pitch.
    /// </summary>
    /// <param name="start">Note start time.</param>
    /// <param name="duration">Note duration; must not be negative.</param>
    /// <param name="midiNote">Target MIDI note number, 0-127.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="duration"/> is negative or <paramref name="midiNote"/> is out of range.
    /// </exception>
    public static ReferenceNote Create(TimeSpan start, TimeSpan duration, int midiNote)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must not be negative.");
        }

        if (midiNote is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(midiNote), midiNote, "MIDI note must be 0-127.");
        }

        return new ReferenceNote { Start = start, End = start + duration, MidiNote = midiNote };
    }
}
