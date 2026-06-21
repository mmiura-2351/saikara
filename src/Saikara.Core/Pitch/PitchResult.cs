using Saikara.Core.Music;

namespace Saikara.Core.Pitch;

/// <summary>
/// The outcome of running a monophonic <see cref="IPitchDetector"/> over a single frame of
/// audio: the estimated fundamental frequency together with a confidence ("clarity") value
/// and whether the frame was judged to contain a pitched (voiced) signal at all.
/// </summary>
/// <remarks>
/// This type is immutable and allocation-free; a detector returns one per analysed frame.
/// For an unvoiced frame use <see cref="Unvoiced"/> (frequency and clarity are both zero);
/// for a voiced frame use <see cref="Voiced"/>.
/// </remarks>
public readonly record struct PitchResult
{
    /// <summary>
    /// The estimated fundamental frequency in hertz, or <c>0</c> when the frame is unvoiced.
    /// </summary>
    public double Frequency { get; }

    /// <summary>
    /// A confidence value in <c>[0, 1]</c>: how strongly periodic the frame was at the
    /// detected period (1 == a perfectly clean tone). For MPM this is the height of the
    /// chosen normalized-square-difference key maximum. Zero when unvoiced.
    /// </summary>
    public double Clarity { get; }

    /// <summary>
    /// <c>true</c> when the frame contained a pitched signal whose period fell in range and
    /// whose clarity cleared the detector's threshold; <c>false</c> for silence, noise, or an
    /// out-of-range pitch. When <c>false</c>, <see cref="Frequency"/> is <c>0</c>.
    /// </summary>
    public bool IsVoiced { get; }

    private PitchResult(double frequency, double clarity, bool isVoiced)
    {
        Frequency = frequency;
        Clarity = clarity;
        IsVoiced = isVoiced;
    }

    /// <summary>
    /// A shared result representing "no pitch detected": frequency and clarity both zero,
    /// <see cref="IsVoiced"/> false.
    /// </summary>
    public static PitchResult Unvoiced { get; } = new(0.0, 0.0, false);

    /// <summary>
    /// Creates a voiced result for a detected pitch.
    /// </summary>
    /// <param name="frequency">The fundamental frequency in hertz; must be positive.</param>
    /// <param name="clarity">The clarity/confidence in <c>[0, 1]</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="frequency"/> is not positive.
    /// </exception>
    public static PitchResult Voiced(double frequency, double clarity)
    {
        if (frequency <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must be positive.");
        return new PitchResult(frequency, clarity, true);
    }

    /// <summary>
    /// Maps a voiced result to a (possibly fractional) MIDI note number via
    /// <see cref="MusicMath.FrequencyToMidiNote(double)"/>, or returns <c>null</c> when
    /// unvoiced. The fractional part expresses how sharp/flat the pitch is (1.0 == a semitone),
    /// which is exactly what the P3 pitch bar and P4 scoring consume.
    /// </summary>
    public double? ToMidiNote()
        => IsVoiced ? MusicMath.FrequencyToMidiNote(Frequency) : null;
}
