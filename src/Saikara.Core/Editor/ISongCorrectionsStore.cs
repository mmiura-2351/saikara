namespace Saikara.Core.Editor;

/// <summary>
/// Persistence-agnostic store for per-song <see cref="SongCorrections"/>. Implementations
/// persist and retrieve correction overrides keyed by karaoke song number; this interface
/// deliberately exposes no storage-specific types so the UI and other services can depend
/// on it without knowing about SQLite.
/// </summary>
public interface ISongCorrectionsStore
{
    /// <summary>
    /// Creates the backing schema if it does not already exist. Safe to call repeatedly
    /// (idempotent / migration-safe); call once at startup before any other operation.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the corrections for the given karaoke number, or <see langword="null"/>
    /// when no corrections have been saved for that song.
    /// </summary>
    /// <param name="songNumber">The karaoke number to look up.</param>
    Task<SongCorrections?> GetAsync(string songNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves (inserts or updates) the corrections for the given karaoke number. Any
    /// previously saved corrections for that number are replaced.
    /// </summary>
    /// <param name="songNumber">The karaoke number to store corrections for.</param>
    /// <param name="corrections">The corrections to persist.</param>
    Task SaveAsync(string songNumber, SongCorrections corrections, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes any saved corrections for the given karaoke number. No-op if no corrections
    /// exist for that number.
    /// </summary>
    /// <param name="songNumber">The karaoke number whose corrections should be removed.</param>
    Task DeleteAsync(string songNumber, CancellationToken cancellationToken = default);
}
