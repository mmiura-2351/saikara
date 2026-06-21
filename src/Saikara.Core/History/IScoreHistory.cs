namespace Saikara.Core.History;

/// <summary>
/// Persistence-agnostic store of past <see cref="ScoreRecord"/>s so singers can review their
/// scores. Implementations persist records and answer history queries; this interface
/// deliberately exposes no storage-specific types so the UI and other services can depend on
/// it without knowing about SQLite.
/// </summary>
public interface IScoreHistory
{
    /// <summary>
    /// Creates the backing schema if it does not already exist. Safe to call repeatedly
    /// (idempotent / migration-safe); call once at startup before any other operation.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a score record. The supplied <see cref="ScoreRecord.Id"/> is ignored on insert.
    /// </summary>
    /// <param name="record">The record to store.</param>
    /// <returns>The stored record, including the assigned <see cref="ScoreRecord.Id"/>.</returns>
    Task<ScoreRecord> AddAsync(ScoreRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent score records across all songs, newest first, capped at
    /// <paramref name="limit"/>.
    /// </summary>
    /// <param name="limit">Maximum number of records to return. Values &lt;= 0 yield an empty list.</param>
    Task<IReadOnlyList<ScoreRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every score record for the given karaoke number, newest first.
    /// </summary>
    /// <param name="songNumber">The karaoke number to filter on.</param>
    Task<IReadOnlyList<ScoreRecord>> GetForSongAsync(string songNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the best (highest <see cref="ScoreRecord.Overall"/>) record for the given karaoke
    /// number, or <see langword="null"/> when the song has no recorded scores.
    /// </summary>
    /// <param name="songNumber">The karaoke number to look up.</param>
    Task<ScoreRecord?> GetBestAsync(string songNumber, CancellationToken cancellationToken = default);
}
