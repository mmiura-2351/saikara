namespace Saikara.Core.Scoring;

/// <summary>
/// Scores a sung performance against a reference melody, DAM-style. Implementations are
/// platform-agnostic, pure and deterministic: the same inputs always yield the same
/// <see cref="ScoreResult"/>. Scoring runs once at the end of a song (no real-time requirement);
/// the result drives the result screen.
/// </summary>
public interface IScoringEngine
{
    /// <summary>
    /// Scores <paramref name="sung"/> against <paramref name="reference"/>.
    /// </summary>
    /// <param name="sung">
    /// The singer's pitch samples, in non-decreasing <see cref="PitchSample.Time"/> order, with
    /// times already aligned to the song's playback clock (latency-corrected).
    /// </param>
    /// <param name="reference">The reference melody, e.g. from <see cref="ReferenceMelody.FromTrack"/>.</param>
    /// <param name="options">Tuning and weights; <see langword="null"/> uses <see cref="ScoringOptions.Default"/>.</param>
    /// <returns>
    /// The score. When either input is empty, <see cref="ScoreResult.Empty"/> (all zeros) is
    /// returned rather than throwing.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sung"/> or <paramref name="reference"/> is <see langword="null"/>.</exception>
    ScoreResult Score(
        IReadOnlyList<PitchSample> sung,
        IReadOnlyList<ReferenceNote> reference,
        ScoringOptions? options = null);
}
