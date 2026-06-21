namespace Saikara.Core.Library;

/// <summary>
/// Persistence-agnostic song library. Implementations store and query <see cref="Song"/>
/// records; this interface deliberately exposes no storage-specific types so the UI and
/// other services can depend on it without knowing about SQLite. Powers the operator
/// song-select remote (search by number / title / artist) and the reservation queue.
/// </summary>
public interface ISongLibrary
{
    /// <summary>
    /// Creates the backing schema if it does not already exist. Safe to call repeatedly
    /// (idempotent / migration-safe); call once at startup before any other operation.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new song or updates the existing one with the same
    /// <see cref="Song.Number"/>. No duplicate row is created for a repeated number.
    /// </summary>
    /// <param name="song">The song to store. Its <see cref="Song.Id"/> is ignored on insert.</param>
    /// <returns>The stored song, including the assigned <see cref="Song.Id"/>.</returns>
    Task<Song> UpsertAsync(Song song, CancellationToken cancellationToken = default);

    /// <summary>Gets the song with the given database id, or <see langword="null"/> if none.</summary>
    Task<Song?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Gets the song with the given karaoke number, or <see langword="null"/> if none.</summary>
    Task<Song?> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>Gets every song in the library, ordered by karaoke number.</summary>
    Task<IReadOnlyList<Song>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Case-insensitive substring search across <see cref="Song.Number"/>,
    /// <see cref="Song.Title"/> and <see cref="Song.Artist"/>. An empty or whitespace
    /// query returns all songs. This backs the song-select remote.
    /// </summary>
    Task<IReadOnlyList<Song>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
