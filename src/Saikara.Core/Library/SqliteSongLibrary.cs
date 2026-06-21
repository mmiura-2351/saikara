using Microsoft.Data.Sqlite;

namespace Saikara.Core.Library;

/// <summary>
/// SQLite-backed <see cref="ISongLibrary"/> using Microsoft.Data.Sqlite. All SQL and the
/// schema live here; callers depend only on <see cref="ISongLibrary"/> and never see a
/// SQLite type.
/// </summary>
/// <remarks>
/// A single connection is opened lazily and kept open for the lifetime of the instance.
/// Keeping one connection open matters for in-memory databases (<c>Data Source=:memory:</c>
/// or shared-cache memory), whose contents vanish once the last connection closes; reusing
/// one connection lets the schema and data persist across calls. Dispose the instance to
/// release the connection (and, for a private in-memory DB, the data).
/// </remarks>
public sealed class SqliteSongLibrary : ISongLibrary, IDisposable, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;

    /// <summary>
    /// Creates a library over the given SQLite connection string or database file path.
    /// A value without an <c>=</c> is treated as a file path and wrapped as
    /// <c>Data Source=&lt;path&gt;</c>; otherwise it is used as a full connection string
    /// (e.g. <c>Data Source=:memory:</c> or
    /// <c>Data Source=test1;Mode=Memory;Cache=Shared</c>).
    /// </summary>
    /// <param name="connectionStringOrPath">A SQLite connection string or a database file path.</param>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    public SqliteSongLibrary(string connectionStringOrPath)
    {
        if (string.IsNullOrWhiteSpace(connectionStringOrPath))
            throw new ArgumentException("A connection string or database path is required.", nameof(connectionStringOrPath));

        _connectionString = connectionStringOrPath.Contains('=', StringComparison.Ordinal)
            ? connectionStringOrPath
            : $"Data Source={connectionStringOrPath}";
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // CREATE TABLE IF NOT EXISTS makes this idempotent / safe to re-run on an
            // existing database, so it doubles as the (currently trivial) migration step.
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS Songs (
                    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    Number           TEXT    NOT NULL UNIQUE,
                    Title            TEXT    NOT NULL,
                    Artist           TEXT    NOT NULL,
                    FilePath         TEXT    NOT NULL,
                    MelodyTrackIndex INTEGER NULL,
                    DateAdded        TEXT    NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Song> UpsertAsync(Song song, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song);

        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Upsert keyed on the unique Number. On conflict we update the mutable columns
            // but keep the original Id (and original DateAdded). RETURNING hands back the
            // full stored row, including the autoincremented Id for new rows.
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO Songs (Number, Title, Artist, FilePath, MelodyTrackIndex, DateAdded)
                VALUES ($number, $title, $artist, $filePath, $melodyTrackIndex, $dateAdded)
                ON CONFLICT(Number) DO UPDATE SET
                    Title            = excluded.Title,
                    Artist           = excluded.Artist,
                    FilePath         = excluded.FilePath,
                    MelodyTrackIndex = excluded.MelodyTrackIndex
                RETURNING Id, Number, Title, Artist, FilePath, MelodyTrackIndex, DateAdded;
                """;
            command.Parameters.AddWithValue("$number", song.Number);
            command.Parameters.AddWithValue("$title", song.Title);
            command.Parameters.AddWithValue("$artist", song.Artist);
            command.Parameters.AddWithValue("$filePath", song.FilePath);
            command.Parameters.AddWithValue("$melodyTrackIndex", (object?)song.MelodyTrackIndex ?? DBNull.Value);
            command.Parameters.AddWithValue("$dateAdded", FormatTimestamp(song.DateAdded));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return ReadSong(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<Song?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => QuerySingleAsync(
            "SELECT Id, Number, Title, Artist, FilePath, MelodyTrackIndex, DateAdded FROM Songs WHERE Id = $id;",
            ("$id", id),
            cancellationToken);

    /// <inheritdoc />
    public Task<Song?> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
        => QuerySingleAsync(
            "SELECT Id, Number, Title, Artist, FilePath, MelodyTrackIndex, DateAdded FROM Songs WHERE Number = $number;",
            ("$number", number),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Song>> GetAllAsync(CancellationToken cancellationToken = default)
        => QueryManyAsync(
            "SELECT Id, Number, Title, Artist, FilePath, MelodyTrackIndex, DateAdded FROM Songs ORDER BY Number;",
            command => { },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Song>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAllAsync(cancellationToken);

        // LIKE is case-insensitive for ASCII in SQLite by default. To get culture-aware
        // case-insensitivity for non-ASCII text too, we fold both sides with LOWER().
        var pattern = "%" + EscapeLike(query.ToLowerInvariant()) + "%";
        return QueryManyAsync(
            """
            SELECT Id, Number, Title, Artist, FilePath, MelodyTrackIndex, DateAdded
            FROM Songs
            WHERE LOWER(Number) LIKE $pattern ESCAPE '\'
               OR LOWER(Title)  LIKE $pattern ESCAPE '\'
               OR LOWER(Artist) LIKE $pattern ESCAPE '\'
            ORDER BY Number;
            """,
            command => command.Parameters.AddWithValue("$pattern", pattern),
            cancellationToken);
    }

    private async Task<Song?> QuerySingleAsync(
        string sql,
        (string Name, object Value) parameter,
        CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            return ReadSong(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<Song>> QueryManyAsync(
        string sql,
        Action<SqliteCommand> bind,
        CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            bind(command);

            var results = new List<Song>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                results.Add(ReadSong(reader));
            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
            return _connection;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is null)
            {
                var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                _connection = connection;
            }
        }
        finally
        {
            _gate.Release();
        }

        return _connection;
    }

    private static Song ReadSong(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Number = reader.GetString(1),
        Title = reader.GetString(2),
        Artist = reader.GetString(3),
        FilePath = reader.GetString(4),
        MelodyTrackIndex = reader.IsDBNull(5) ? null : reader.GetInt32(5),
        DateAdded = DateTimeOffset.Parse(
            reader.GetString(6),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
    };

    private static string FormatTimestamp(DateTimeOffset value)
        => value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static string EscapeLike(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    /// <inheritdoc />
    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        _gate.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
        _gate.Dispose();
    }
}
