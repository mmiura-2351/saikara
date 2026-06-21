using System.Globalization;
using Saikara.Core.History;

namespace Saikara.App.ViewModels;

/// <summary>
/// A display projection of a persisted <see cref="ScoreRecord"/> for the result overlay's recent
/// list (P5). Pre-formats the few fields the list shows so the XAML binds plain strings and needs
/// no value converters. Immutable; built via <see cref="FromRecord"/>.
/// </summary>
public sealed class ScoreHistoryItem
{
    private ScoreHistoryItem(string scoreText, string gradeText, string dateText)
    {
        ScoreText = scoreText;
        GradeText = gradeText;
        DateText = dateText;
    }

    /// <summary>The overall score rounded to a whole number, e.g. <c>"87"</c>.</summary>
    public string ScoreText { get; }

    /// <summary>The coarse letter grade, e.g. <c>"A"</c>.</summary>
    public string GradeText { get; }

    /// <summary>The scored-at date in the singer's local time, e.g. <c>"2026-06-21 14:30"</c>.</summary>
    public string DateText { get; }

    /// <summary>
    /// Builds a list item from a stored record. The overall score is rounded for display; the
    /// timestamp is shown in local time (the stored offset is preserved on disk, but the singer
    /// cares about wall-clock here). Culture is supplied so the whole overlay formats consistently.
    /// </summary>
    public static ScoreHistoryItem FromRecord(ScoreRecord record, CultureInfo culture)
    {
        int overall = (int)System.Math.Round(record.Overall, System.MidpointRounding.AwayFromZero);
        return new ScoreHistoryItem(
            overall.ToString(culture),
            record.Grade,
            record.ScoredAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm", culture));
    }
}
