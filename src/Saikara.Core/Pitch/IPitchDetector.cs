namespace Saikara.Core.Pitch;

/// <summary>
/// A monophonic pitch detector: estimates the fundamental frequency of a single frame of
/// mono audio samples. Implementations are platform-agnostic and contain no audio-device I/O;
/// the mic-capture layer (WASAPI in <c>Saikara.App</c>) slices the input stream into frames
/// and calls <see cref="Detect"/> per frame to drive the real-time pitch bar and scoring.
/// </summary>
public interface IPitchDetector
{
    /// <summary>
    /// Analyses one frame of mono PCM samples and returns the detected pitch.
    /// </summary>
    /// <param name="samples">
    /// Mono audio samples, nominally in <c>[-1, 1]</c>. The whole span is treated as a single
    /// analysis frame; the caller chooses the frame size (see the type-level remarks of the
    /// concrete detector for guidance). Implementations do not retain or mutate the span.
    /// </param>
    /// <param name="sampleRate">The sample rate of <paramref name="samples"/> in hertz; must be positive.</param>
    /// <returns>
    /// A voiced <see cref="PitchResult"/> when a pitch is found, otherwise
    /// <see cref="PitchResult.Unvoiced"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="sampleRate"/> is not positive.
    /// </exception>
    PitchResult Detect(ReadOnlySpan<float> samples, int sampleRate);
}
