using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Saikara.Core.History;

/// <summary>
/// SQLite-backed <see cref="IScoreHistory"/> using Microsoft.Data.Sqlite. All SQL and the
/// schema live here; callers depend only on <see cref="IScoreHistory"/> and never see a
/// SQLite type.
/// </summary>
/// <remarks>
/// A single connection is opened lazily and kept open for the lifetime of the instance.
/// Keeping one connection open matters for in-memory databases (<c>Data Source=:memory:</c>
/// or shared-cache memory), whose contents vanish once the last connection closes; reusing
/// one connection lets the schema and data persist across calls. Dispose the instance to
/// release the connection (and, for a private in-memory DB, the data).
/// </remarks>
public sealed class SqliteScoreHistory : IScoreHistory, IDisposable, IAsyncDisposable
{
    // The full set of columns, in a fixed order shared by INSERT ... RETURNING and SELECTs so
    // the reader offsets in ReadRecord stay in sync with every query.
    private const string Columns =
        "Id, SongNumber, SongTitle, SongArtist, ScoredAt, Overall, PitchAccuracy, Stability, " +
        "Expression, LongTone, VibratoCount, ShakuriCount, KobushiCount, Grade, KeyOffset, TempoPercent";

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;

    /// <summary>
    /// Creates a history store over the given SQLite connection string or database file path.
    /// A value without an <c>=</c> is treated as a file path and wrapped as
    /// <c>Data Source=&lt;path&gt;</c>; otherwise it is used as a full connection string
    /// (e.g. <c>Data Source=:memory:</c> or
    /// <c>Data Source=test1;Mode=Memory;Cache=Shared</c>).
    /// </summary>
    /// <param name="connectionStringOrPath">A SQLite connection string or a database file path.</param>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    public SqliteScoreHistory(string connectionStringOrPath)
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
            // ScoredAt is stored as an ISO-8601 round-trip ("O") string so the exact instant
            // and original UTC offset survive a round-trip. An index on SongNumber keeps the
            // per-song history and "best" lookups cheap.
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS Scores (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    SongNumber    TEXT    NOT NULL,
                    SongTitle     TEXT    NOT NULL,
                    SongArtist    TEXT    NOT NULL,
                    ScoredAt      TEXT    NOT NULL,
                    Overall       REAL    NOT NULL,
                    PitchAccuracy REAL    NOT NULL,
                    Stability     REAL    NOT NULL,
                    Expression    REAL    NOT NULL,
                    LongTone      REAL    NOT NULL,
                    VibratoCount  INTEGER NOT NULL,
                    ShakuriCount  INTEGER NOT NULL,
                    KobushiCount  INTEGER NOT NULL,
                    Grade         TEXT    NOT NULL,
                    KeyOffset     INTEGER NOT NULL,
                    TempoPercent  REAL    NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Scores_SongNumber ON Scores (SongNumber);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ScoreRecord> AddAsync(ScoreRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // RETURNING hands back the full stored row, including the autoincremented Id.
            var command = connection.CreateCommand();
            command.CommandText =
                $"""
                INSERT INTO Scores (
                    SongNumber, SongTitle, SongArtist, ScoredAt, Overall, PitchAccuracy, Stability,
                    Expression, LongTone, VibratoCount, ShakuriCount, KobushiCount, Grade, KeyOffset, TempoPercent)
                VALUES (
                    $songNumber, $songTitle, $songArtist, $scoredAt, $overall, $pitchAccuracy, $stability,
                    $expression, $longTone, $vibratoCount, $shakuriCount, $kobushiCount, $grade, $keyOffset, $tempoPercent)
                RETURNING {Columns};
                """;
            command.Parameters.AddWithValue("$songNumber", record.SongNumber);
            command.Parameters.AddWithValue("$songTitle", record.SongTitle);
            command.Parameters.AddWithValue("$songArtist", record.SongArtist);
            command.Parameters.AddWithValue("$scoredAt", FormatTimestamp(record.ScoredAt));
            command.Parameters.AddWithValue("$overall", record.Overall);
            command.Parameters.AddWithValue("$pitchAccuracy", record.PitchAccuracy);
            command.Parameters.AddWithValue("$stability", record.Stability);
            command.Parameters.AddWithValue("$expression", record.Expression);
            command.Parameters.AddWithValue("$longTone", record.LongTone);
            command.Parameters.AddWithValue("$vibratoCount", record.VibratoCount);
            command.Parameters.AddWithValue("$shakuriCount", record.ShakuriCount);
            command.Parameters.AddWithValue("$kobushiCount", record.KobushiCount);
            command.Parameters.AddWithValue("$grade", record.Grade);
            command.Parameters.AddWithValue("$keyOffset", record.KeyOffset);
            command.Parameters.AddWithValue("$tempoPercent", record.TempoPercent);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return ReadRecord(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScoreRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            return Task.FromResult<IReadOnlyList<ScoreRecord>>(Array.Empty<ScoreRecord>());

        // Tie-break on Id DESC so records sharing a timestamp keep insertion order (newest first).
        return QueryManyAsync(
            $"SELECT {Columns} FROM Scores ORDER BY ScoredAt DESC, Id DESC LIMIT $limit;",
            command => command.Parameters.AddWithValue("$limit", limit),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScoreRecord>> GetForSongAsync(string songNumber, CancellationToken cancellationToken = default)
        => QueryManyAsync(
            $"SELECT {Columns} FROM Scores WHERE SongNumber = $songNumber ORDER BY ScoredAt DESC, Id DESC;",
            command => command.Parameters.AddWithValue("$songNumber", songNumber ?? string.Empty),
            cancellationToken);

    /// <inheritdoc />
    public async Task<ScoreRecord?> GetBestAsync(string songNumber, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Highest Overall wins; on a tie, prefer the most recent (then newest Id).
            var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT {Columns} FROM Scores WHERE SongNumber = $songNumber ORDER BY Overall DESC, ScoredAt DESC, Id DESC LIMIT 1;";
            command.Parameters.AddWithValue("$songNumber", songNumber ?? string.Empty);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            return ReadRecord(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ScoreRecord>> QueryManyAsync(
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

            var results = new List<ScoreRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                results.Add(ReadRecord(reader));
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

    private static ScoreRecord ReadRecord(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        SongNumber = reader.GetString(1),
        SongTitle = reader.GetString(2),
        SongArtist = reader.GetString(3),
        ScoredAt = DateTimeOffset.Parse(
            reader.GetString(4),
            CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind),
        Overall = reader.GetDouble(5),
        PitchAccuracy = reader.GetDouble(6),
        Stability = reader.GetDouble(7),
        Expression = reader.GetDouble(8),
        LongTone = reader.GetDouble(9),
        VibratoCount = reader.GetInt32(10),
        ShakuriCount = reader.GetInt32(11),
        KobushiCount = reader.GetInt32(12),
        Grade = reader.GetString(13),
        KeyOffset = reader.GetInt32(14),
        TempoPercent = reader.GetDouble(15),
    };

    private static string FormatTimestamp(DateTimeOffset value)
        => value.ToString("O", CultureInfo.InvariantCulture);

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
