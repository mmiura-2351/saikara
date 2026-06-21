using Saikara.Core.Scoring;

namespace Saikara.Core.History;

/// <summary>
/// A persisted scoring outcome: the DAM-style sub-scores and technique counts from a single
/// performance, together with the song it was sung over and the key/tempo it was sung at.
/// This is the platform-agnostic record stored by <see cref="IScoreHistory"/> so singers can
/// review their past scores. It is built from a <see cref="ScoreResult"/> via
/// <see cref="FromScoreResult"/>.
/// </summary>
public sealed record ScoreRecord
{
    /// <summary>
    /// Stable database identity (autoincrement primary key). Zero means "not yet stored";
    /// a persisted <see cref="ScoreRecord"/> always has a positive <see cref="Id"/>.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Karaoke selection number of the song that was sung. May be empty for an ad-hoc file
    /// opened directly (i.e. not selected from the library by number); history for such a
    /// performance is still recorded, but <see cref="IScoreHistory.GetForSongAsync"/> /
    /// <see cref="IScoreHistory.GetBestAsync"/> key on a non-empty number.
    /// </summary>
    public required string SongNumber { get; init; }

    /// <summary>Title of the song that was sung, captured for display in the history list.</summary>
    public required string SongTitle { get; init; }

    /// <summary>Performing artist of the song that was sung, captured for display.</summary>
    public required string SongArtist { get; init; }

    /// <summary>
    /// When the performance was scored. Supplied by the caller (e.g. <c>DateTimeOffset.Now</c>
    /// at the result screen); <see cref="Saikara.Core"/> never reads the clock itself. Stored
    /// losslessly and round-trips with its original offset.
    /// </summary>
    public required DateTimeOffset ScoredAt { get; init; }

    /// <summary>The overall score, 0-100. See <see cref="ScoreResult.Overall"/>.</summary>
    public required double Overall { get; init; }

    /// <summary>Pitch accuracy (音程) sub-score, 0-100. See <see cref="ScoreResult.PitchAccuracy"/>.</summary>
    public required double PitchAccuracy { get; init; }

    /// <summary>Stability (安定性) sub-score, 0-100. See <see cref="ScoreResult.Stability"/>.</summary>
    public required double Stability { get; init; }

    /// <summary>Expression (抑揚) sub-score, 0-100. See <see cref="ScoreResult.Expression"/>.</summary>
    public required double Expression { get; init; }

    /// <summary>Long tone (ロングトーン) sub-score, 0-100. See <see cref="ScoreResult.LongTone"/>.</summary>
    public required double LongTone { get; init; }

    /// <summary>Number of vibrato (ビブラート) ornaments detected.</summary>
    public required int VibratoCount { get; init; }

    /// <summary>Number of shakuri (しゃくり) onset slides detected.</summary>
    public required int ShakuriCount { get; init; }

    /// <summary>Number of kobushi (こぶし) bend-and-return ornaments detected.</summary>
    public required int KobushiCount { get; init; }

    /// <summary>The coarse letter grade derived from <see cref="Overall"/>. See <see cref="ScoreResult.Grade"/>.</summary>
    public required string Grade { get; init; }

    /// <summary>Key offset (in semitones) the song was transposed by during the performance.</summary>
    public required int KeyOffset { get; init; }

    /// <summary>Tempo (as a percentage of the original, e.g. <c>100</c> = original) of the performance.</summary>
    public required double TempoPercent { get; init; }

    /// <summary>
    /// Builds a <see cref="ScoreRecord"/> from a freshly computed <see cref="ScoreResult"/> and
    /// the surrounding performance context (the song that was sung, when it was scored, and the
    /// key/tempo it was sung at). The grade is taken from <see cref="ScoreResult.Grade"/>.
    /// </summary>
    /// <param name="result">The scoring outcome to persist.</param>
    /// <param name="songNumber">Karaoke number of the song (empty for an ad-hoc opened file).</param>
    /// <param name="songTitle">Title of the song that was sung.</param>
    /// <param name="songArtist">Artist of the song that was sung.</param>
    /// <param name="scoredAt">When the performance was scored (supplied by the caller).</param>
    /// <param name="keyOffset">Key offset in semitones the song was transposed by.</param>
    /// <param name="tempoPercent">Tempo as a percentage of the original.</param>
    /// <returns>A <see cref="ScoreRecord"/> ready to pass to <see cref="IScoreHistory.AddAsync"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result"/> is null.</exception>
    public static ScoreRecord FromScoreResult(
        ScoreResult result,
        string songNumber,
        string songTitle,
        string songArtist,
        DateTimeOffset scoredAt,
        int keyOffset,
        double tempoPercent)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ScoreRecord
        {
            SongNumber = songNumber ?? string.Empty,
            SongTitle = songTitle ?? string.Empty,
            SongArtist = songArtist ?? string.Empty,
            ScoredAt = scoredAt,
            Overall = result.Overall,
            PitchAccuracy = result.PitchAccuracy,
            Stability = result.Stability,
            Expression = result.Expression,
            LongTone = result.LongTone,
            VibratoCount = result.VibratoCount,
            ShakuriCount = result.ShakuriCount,
            KobushiCount = result.KobushiCount,
            Grade = result.Grade,
            KeyOffset = keyOffset,
            TempoPercent = tempoPercent,
        };
    }
}
