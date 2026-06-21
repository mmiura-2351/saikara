namespace Saikara.Core.Pitch;

/// <summary>
/// Tunable parameters for <see cref="McLeodPitchDetector"/>. The defaults target a sung human
/// voice: roughly the range of a bass low note up to a high soprano, with a clarity threshold
/// strict enough to reject noise but lenient enough for a real, slightly-noisy mic signal.
/// </summary>
public sealed record PitchDetectorOptions
{
    /// <summary>
    /// The lowest detectable fundamental in hertz. Pitches whose period exceeds this (i.e. a
    /// lower frequency) are reported as <see cref="PitchResult.Unvoiced"/>. Default 70 Hz
    /// (about C#2), comfortably below a typical male singing range. Must be positive.
    /// </summary>
    public double MinFrequency { get; init; } = 70.0;

    /// <summary>
    /// The highest detectable fundamental in hertz. Pitches above this are reported as
    /// <see cref="PitchResult.Unvoiced"/>. Default 1100 Hz (about C#6), above a typical female
    /// singing range. Must be greater than <see cref="MinFrequency"/>.
    /// </summary>
    public double MaxFrequency { get; init; } = 1100.0;

    /// <summary>
    /// The minimum NSDF key-maximum height a frame must reach to be considered voiced, in
    /// <c>[0, 1]</c>. Higher is stricter. Default 0.9 — a good balance for a clean tone while
    /// still rejecting noise and silence.
    /// </summary>
    public double ClarityThreshold { get; init; } = 0.9;
}
