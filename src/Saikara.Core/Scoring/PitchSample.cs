namespace Saikara.Core.Scoring;

/// <summary>
/// One analysis hop of the singer's microphone input, produced by pairing an
/// <see cref="Saikara.Core.Pitch.IPitchDetector"/> result with the playback time it corresponds to.
/// A whole performance is scored from a time-ordered sequence of these samples.
/// </summary>
/// <remarks>
/// The mic-capture layer (WASAPI in <c>Saikara.App</c>) slices its input into frames, runs the
/// detector per frame and maps each frame to a <see cref="Time"/> on the song's playback clock
/// (subtracting capture latency so the sample lines up with the backing track). <see cref="Energy"/>
/// is an optional loudness channel used only by the expression (抑揚) metric; set it to <c>0</c>
/// (the default) when loudness is unknown, in which case scoring treats expression as neutral
/// rather than penalising it.
/// </remarks>
public readonly record struct PitchSample
{
    /// <summary>
    /// The playback position (from the start of the song) this hop corresponds to, already
    /// corrected for capture latency. Samples are expected in non-decreasing <see cref="Time"/> order.
    /// </summary>
    public TimeSpan Time { get; init; }

    /// <summary>
    /// The detected fundamental frequency in hertz, or <c>0</c> when the hop is unvoiced
    /// (see <see cref="IsVoiced"/>).
    /// </summary>
    public double Frequency { get; init; }

    /// <summary>
    /// <c>true</c> when the detector judged this hop to contain a pitched (sung) signal;
    /// <c>false</c> for silence, noise, or an out-of-range pitch. When <c>false</c>,
    /// <see cref="Frequency"/> is <c>0</c> and the hop is ignored by pitch-based metrics.
    /// </summary>
    public bool IsVoiced { get; init; }

    /// <summary>
    /// Detector confidence in <c>[0, 1]</c> (the pitch detector's clarity). Reserved for
    /// future weighting; not required to be set.
    /// </summary>
    public double Clarity { get; init; }

    /// <summary>
    /// Loudness of this hop in <c>[0, 1]</c>, or <c>0</c> when loudness is unknown. Drives the
    /// expression (抑揚) metric. A performance whose samples are all <c>0</c> is treated as
    /// "energy unknown" and scored neutrally for expression rather than penalised.
    /// </summary>
    public double Energy { get; init; }

    /// <summary>
    /// Creates a voiced sample at <paramref name="time"/> with the given frequency.
    /// </summary>
    /// <param name="time">Latency-corrected playback time of the hop.</param>
    /// <param name="frequency">Detected fundamental in hertz; must be positive.</param>
    /// <param name="clarity">Detector confidence in <c>[0, 1]</c>.</param>
    /// <param name="energy">Loudness in <c>[0, 1]</c>, or <c>0</c> when unknown.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="frequency"/> is not positive.</exception>
    public static PitchSample VoicedAt(TimeSpan time, double frequency, double clarity = 1.0, double energy = 0.0)
    {
        if (frequency <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must be positive.");
        }

        return new PitchSample
        {
            Time = time,
            Frequency = frequency,
            IsVoiced = true,
            Clarity = clarity,
            Energy = energy,
        };
    }

    /// <summary>
    /// Creates an unvoiced sample at <paramref name="time"/> (silence/noise). <see cref="Frequency"/>
    /// is <c>0</c>; <paramref name="energy"/> may still be set so quiet passages can inform expression.
    /// </summary>
    public static PitchSample UnvoicedAt(TimeSpan time, double energy = 0.0)
        => new()
        {
            Time = time,
            Frequency = 0.0,
            IsVoiced = false,
            Clarity = 0.0,
            Energy = energy,
        };
}
