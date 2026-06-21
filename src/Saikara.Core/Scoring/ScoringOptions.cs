namespace Saikara.Core.Scoring;

/// <summary>
/// Tunable parameters and sub-score weights for <see cref="ScoringEngine"/>. The defaults aim at
/// a forgiving-but-honest DAM-style profile for an amateur singer. All weights are relative; the
/// engine normalises the four base metrics by their total weight and then adds the capped
/// technique bonuses, so changing one weight does not silently rescale the whole result.
/// </summary>
public sealed record ScoringOptions
{
    /// <summary>
    /// Half-width of the "on pitch" window in cents. A sung pitch whose absolute cents error to
    /// the target note is at or below this counts as correct. Default 50 cents (a quarter tone).
    /// </summary>
    public double PitchToleranceCents { get; init; } = 50.0;

    /// <summary>
    /// When <c>true</c> (default), pitch error is octave-folded: singing the right pitch class in
    /// the wrong octave still scores. When <c>false</c>, the octave must match too. DAM scoring is
    /// effectively octave-agnostic, which also makes scoring robust to octave errors in the pitch
    /// detector.
    /// </summary>
    public bool OctaveAgnostic { get; init; } = true;

    /// <summary>
    /// Minimum reference-note duration, in seconds, for a note to count as a long tone for the
    /// long-tone (ロングトーン) metric and for vibrato/kobushi detection. Shorter notes are ignored
    /// by those metrics. Default 1.0 s.
    /// </summary>
    public double LongToneMinSeconds { get; init; } = 1.0;

    /// <summary>
    /// Lowest vibrato (ビブラート) modulation rate in hertz that counts. Default 4 Hz.
    /// </summary>
    public double VibratoMinRateHz { get; init; } = 4.0;

    /// <summary>
    /// Highest vibrato modulation rate in hertz that counts. Default 8 Hz. Modulation outside
    /// <c>[<see cref="VibratoMinRateHz"/>, <see cref="VibratoMaxRateHz"/>]</c> is not vibrato.
    /// </summary>
    public double VibratoMaxRateHz { get; init; } = 8.0;

    /// <summary>
    /// Minimum peak-to-peak pitch modulation depth, in cents, for vibrato to count. Default 30
    /// cents. Below this the wobble is too shallow to be intentional vibrato.
    /// </summary>
    public double VibratoMinDepthCents { get; init; } = 30.0;

    /// <summary>
    /// Length of the onset window, in seconds, in which a rising approach to the target counts as
    /// shakuri (しゃくり). Default 0.25 s.
    /// </summary>
    public double ShakuriOnsetSeconds { get; init; } = 0.25;

    /// <summary>
    /// Minimum amount, in cents, the sung pitch must start below the target and then rise into it
    /// for a shakuri to count. Default 80 cents.
    /// </summary>
    public double ShakuriMinDepthCents { get; init; } = 80.0;

    /// <summary>
    /// Minimum amount, in cents, of a brief up-and-return bend within a held note for it to count
    /// as kobushi (こぶし). Default 80 cents.
    /// </summary>
    public double KobushiMinDepthCents { get; init; } = 80.0;

    /// <summary>Weight of the pitch-accuracy (音程) metric in the overall score. Default 50.</summary>
    public double PitchWeight { get; init; } = 50.0;

    /// <summary>Weight of the stability (安定性) metric in the overall score. Default 15.</summary>
    public double StabilityWeight { get; init; } = 15.0;

    /// <summary>Weight of the long-tone (ロングトーン) metric in the overall score. Default 10.</summary>
    public double LongToneWeight { get; init; } = 10.0;

    /// <summary>Weight of the expression (抑揚) metric in the overall score. Default 10.</summary>
    public double ExpressionWeight { get; init; } = 10.0;

    /// <summary>Bonus points awarded per detected vibrato, capped by <see cref="VibratoBonusCap"/>. Default 3.</summary>
    public double VibratoBonusPerCount { get; init; } = 3.0;

    /// <summary>Maximum total vibrato bonus added to the overall score. Default 6.</summary>
    public double VibratoBonusCap { get; init; } = 6.0;

    /// <summary>Bonus points awarded per detected shakuri, capped by <see cref="ShakuriBonusCap"/>. Default 1.</summary>
    public double ShakuriBonusPerCount { get; init; } = 1.0;

    /// <summary>Maximum total shakuri bonus added to the overall score. Default 3.</summary>
    public double ShakuriBonusCap { get; init; } = 3.0;

    /// <summary>Bonus points awarded per detected kobushi, capped by <see cref="KobushiBonusCap"/>. Default 1.</summary>
    public double KobushiBonusPerCount { get; init; } = 1.0;

    /// <summary>Maximum total kobushi bonus added to the overall score. Default 2.</summary>
    public double KobushiBonusCap { get; init; } = 2.0;

    /// <summary>A shared instance carrying the defaults documented above.</summary>
    public static ScoringOptions Default { get; } = new();
}
