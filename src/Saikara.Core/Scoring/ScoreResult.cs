namespace Saikara.Core.Scoring;

/// <summary>
/// The deterministic outcome of scoring a performance: an overall 0-100 score plus the
/// DAM-style sub-scores and technique counts that produced it. Returned by
/// <see cref="ScoringEngine.Score"/> and shown on the result screen.
/// </summary>
public sealed record ScoreResult
{
    /// <summary>The overall score, 0-100. A weighted blend of the sub-scores plus capped bonuses.</summary>
    public required double Overall { get; init; }

    /// <summary>
    /// Pitch accuracy (音程), 0-100: the time-weighted fraction of voiced singing inside reference
    /// notes that landed within tolerance of the target pitch.
    /// </summary>
    public required double PitchAccuracy { get; init; }

    /// <summary>
    /// Stability (安定性), 0-100: how steady the pitch was within sustained notes (lower cents
    /// standard deviation scores higher). Neutral when there is nothing sustained to judge.
    /// </summary>
    public required double Stability { get; init; }

    /// <summary>
    /// Expression (抑揚), 0-100: dynamic range/variation derived from the energy channel. A
    /// neutral score is returned when energy is unknown (all samples report <c>0</c> energy), so
    /// a performance is never penalised merely for lacking loudness data.
    /// </summary>
    public required double Expression { get; init; }

    /// <summary>
    /// Long tone (ロングトーン), 0-100: how well notes longer than the configured threshold were
    /// sustained on pitch with low variance. Neutral when the song has no long notes.
    /// </summary>
    public required double LongTone { get; init; }

    /// <summary>The number of vibrato (ビブラート) ornaments detected.</summary>
    public required int VibratoCount { get; init; }

    /// <summary>The number of shakuri (しゃくり) onset slides detected.</summary>
    public required int ShakuriCount { get; init; }

    /// <summary>The number of kobushi (こぶし) bend-and-return ornaments detected.</summary>
    public required int KobushiCount { get; init; }

    /// <summary>
    /// A coarse letter grade derived from <see cref="Overall"/> for display
    /// (S ≥ 90, A ≥ 80, B ≥ 70, C ≥ 60, D ≥ 50, otherwise E).
    /// </summary>
    public string Grade => Overall switch
    {
        >= 90.0 => "S",
        >= 80.0 => "A",
        >= 70.0 => "B",
        >= 60.0 => "C",
        >= 50.0 => "D",
        _ => "E",
    };

    /// <summary>
    /// The defined result for an empty performance or empty reference: every score is <c>0</c>
    /// and every technique count is <c>0</c>. The engine returns this (rather than throwing) when
    /// the sung samples or the reference melody is empty.
    /// </summary>
    public static ScoreResult Empty { get; } = new()
    {
        Overall = 0.0,
        PitchAccuracy = 0.0,
        Stability = 0.0,
        Expression = 0.0,
        LongTone = 0.0,
        VibratoCount = 0,
        ShakuriCount = 0,
        KobushiCount = 0,
    };
}
