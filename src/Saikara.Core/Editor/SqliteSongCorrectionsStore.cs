using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Saikara.Core.Editor;

/// <summary>
/// SQLite-backed <see cref="ISongCorrectionsStore"/> using Microsoft.Data.Sqlite. All SQL
/// and the schema live here; callers depend only on <see cref="ISongCorrectionsStore"/> and
/// never see a SQLite type.
/// </summary>
/// <remarks>
/// A single connection is opened lazily and kept open for the lifetime of the instance.
/// Keeping one connection open matters for in-memory databases (<c>Data Source=:memory:</c>
/// or shared-cache memory), whose contents vanish once the last connection closes; reusing
/// one connection lets the schema and data persist across calls. Dispose the instance to
/// release the connection (and, for a private in-memory DB, the data).
/// </remarks>
public sealed class SqliteSongCorrectionsStore : ISongCorrectionsStore, IDisposable, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;

    /// <summary>
    /// Creates a corrections store over the given SQLite connection string or database file
    /// path. A value without an <c>=</c> is treated as a file path and wrapped as
    /// <c>Data Source=&lt;path&gt;</c>; otherwise it is used as a full connection string
    /// (e.g. <c>Data Source=:memory:</c> or
    /// <c>Data Source=test1;Mode=Memory;Cache=Shared</c>).
    /// </summary>
    /// <param name="connectionStringOrPath">A SQLite connection string or a database file path.</param>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    public SqliteSongCorrectionsStore(string connectionStringOrPath)
    {
        if (string.IsNullOrWhiteSpace(connectionStringOrPath))
            throw new ArgumentException(
                "A connection string or database path is required.",
                nameof(connectionStringOrPath));

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
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS SongCorrections (
                    SongNumber              TEXT NOT NULL PRIMARY KEY,
                    MelodyTrackIndex        INTEGER NULL,
                    LyricOffsetMs           REAL NOT NULL DEFAULT 0,
                    PerSyllableAdjustments  TEXT NULL
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
    public async Task<SongCorrections?> GetAsync(
        string songNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(songNumber);

        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = connection.CreateCommand();
            command.CommandText =
                "SELECT MelodyTrackIndex, LyricOffsetMs, PerSyllableAdjustments " +
                "FROM SongCorrections WHERE SongNumber = $songNumber;";
            command.Parameters.AddWithValue("$songNumber", songNumber);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            return ReadCorrections(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        string songNumber, SongCorrections corrections, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(songNumber);
        ArgumentNullException.ThrowIfNull(corrections);

        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO SongCorrections (SongNumber, MelodyTrackIndex, LyricOffsetMs, PerSyllableAdjustments)
                VALUES ($songNumber, $melodyTrackIndex, $lyricOffsetMs, $perSyllableAdjustments)
                ON CONFLICT(SongNumber) DO UPDATE SET
                    MelodyTrackIndex       = excluded.MelodyTrackIndex,
                    LyricOffsetMs          = excluded.LyricOffsetMs,
                    PerSyllableAdjustments = excluded.PerSyllableAdjustments;
                """;
            command.Parameters.AddWithValue("$songNumber", songNumber);
            command.Parameters.AddWithValue("$melodyTrackIndex",
                (object?)corrections.MelodyTrackIndex ?? DBNull.Value);
            command.Parameters.AddWithValue("$lyricOffsetMs", corrections.LyricOffsetMs);
            command.Parameters.AddWithValue("$perSyllableAdjustments",
                SerializeAdjustments(corrections.PerSyllableAdjustments));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string songNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(songNumber);

        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM SongCorrections WHERE SongNumber = $songNumber;";
            command.Parameters.AddWithValue("$songNumber", songNumber);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

    private static SongCorrections ReadCorrections(SqliteDataReader reader) => new()
    {
        MelodyTrackIndex = reader.IsDBNull(0) ? null : reader.GetInt32(0),
        LyricOffsetMs = reader.GetDouble(1),
        PerSyllableAdjustments = DeserializeAdjustments(
            reader.IsDBNull(2) ? null : reader.GetString(2)),
    };

    private static object SerializeAdjustments(IReadOnlyList<SyllableAdjustment>? adjustments)
    {
        if (adjustments is null || adjustments.Count == 0)
            return DBNull.Value;

        return JsonSerializer.Serialize(adjustments, JsonOptions);
    }

    private static IReadOnlyList<SyllableAdjustment>? DeserializeAdjustments(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        return JsonSerializer.Deserialize<List<SyllableAdjustment>>(json, JsonOptions);
    }

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
