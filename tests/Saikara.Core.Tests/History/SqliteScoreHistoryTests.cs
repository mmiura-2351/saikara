using Saikara.Core.History;
using Saikara.Core.Scoring;

namespace Saikara.Core.Tests.History;

/// <summary>
/// Tests for <see cref="SqliteScoreHistory"/> against a real SQLite database.
/// Each test gets its own unique shared-cache in-memory database; the store keeps a single
/// connection open, so the schema and data survive across calls for the lifetime of the
/// test, and the database is discarded when the store is disposed.
/// </summary>
public sealed class SqliteScoreHistoryTests : IAsyncLifetime, IDisposable
{
    private static int _counter;
    private readonly SqliteScoreHistory _history;

    public SqliteScoreHistoryTests()
    {
        // A unique name per test keeps the shared-cache in-memory databases isolated.
        int id = Interlocked.Increment(ref _counter);
        _history = new SqliteScoreHistory($"Data Source=score-test-{id};Mode=Memory;Cache=Shared");
    }

    public Task InitializeAsync() => _history.InitializeAsync();

    public Task DisposeAsync()
    {
        _history.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _history.Dispose();

    private static readonly DateTimeOffset BaseTime =
        new(2026, 6, 21, 12, 0, 0, TimeSpan.FromHours(9));

    private static ScoreRecord NewRecord(
        string songNumber,
        double overall,
        DateTimeOffset scoredAt,
        string title = "Some Title",
        string artist = "Some Artist",
        int keyOffset = 0,
        double tempoPercent = 100.0)
        => new()
        {
            SongNumber = songNumber,
            SongTitle = title,
            SongArtist = artist,
            ScoredAt = scoredAt,
            Overall = overall,
            PitchAccuracy = overall,
            Stability = overall,
            Expression = overall,
            LongTone = overall,
            VibratoCount = 1,
            ShakuriCount = 2,
            KobushiCount = 3,
            Grade = "A",
            KeyOffset = keyOffset,
            TempoPercent = tempoPercent,
        };

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        // Already initialized once in InitializeAsync(); calling again must not throw
        // and must not disturb existing data.
        await _history.AddAsync(NewRecord("1000-01", 80.0, BaseTime));

        await _history.InitializeAsync();
        await _history.InitializeAsync();

        Assert.Single(await _history.GetRecentAsync(10));
    }

    [Fact]
    public async Task AddAsync_AssignsId_AndRoundTripsAllFields()
    {
        var stored = await _history.AddAsync(NewRecord("1234-56", 88.5, BaseTime));

        Assert.True(stored.Id > 0);

        var recent = await _history.GetForSongAsync("1234-56");
        var fetched = Assert.Single(recent);
        Assert.Equal(stored.Id, fetched.Id);
        Assert.Equal("1234-56", fetched.SongNumber);
        Assert.Equal("Some Title", fetched.SongTitle);
        Assert.Equal("Some Artist", fetched.SongArtist);
        Assert.Equal(88.5, fetched.Overall);
        Assert.Equal(88.5, fetched.PitchAccuracy);
        Assert.Equal(88.5, fetched.Stability);
        Assert.Equal(88.5, fetched.Expression);
        Assert.Equal(88.5, fetched.LongTone);
        Assert.Equal(1, fetched.VibratoCount);
        Assert.Equal(2, fetched.ShakuriCount);
        Assert.Equal(3, fetched.KobushiCount);
        Assert.Equal("A", fetched.Grade);
        Assert.Equal(0, fetched.KeyOffset);
        Assert.Equal(100.0, fetched.TempoPercent);
    }

    [Fact]
    public async Task AddAsync_PreservesEmptySongNumber_ForAdHocFiles()
    {
        var stored = await _history.AddAsync(NewRecord(string.Empty, 70.0, BaseTime));
        Assert.True(stored.Id > 0);
        Assert.Equal(string.Empty, stored.SongNumber);

        // The ad-hoc record still appears in the recent (all-songs) list.
        Assert.Single(await _history.GetRecentAsync(10));
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsMostRecentFirst_RespectingLimit()
    {
        await _history.AddAsync(NewRecord("A", 50.0, BaseTime.AddMinutes(1)));
        await _history.AddAsync(NewRecord("B", 60.0, BaseTime.AddMinutes(3)));
        await _history.AddAsync(NewRecord("C", 70.0, BaseTime.AddMinutes(2)));

        var all = await _history.GetRecentAsync(10);
        Assert.Equal(new[] { "B", "C", "A" }, all.Select(r => r.SongNumber));

        var limited = await _history.GetRecentAsync(2);
        Assert.Equal(new[] { "B", "C" }, limited.Select(r => r.SongNumber));
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsEmpty_ForNonPositiveLimit()
    {
        await _history.AddAsync(NewRecord("A", 50.0, BaseTime));
        Assert.Empty(await _history.GetRecentAsync(0));
        Assert.Empty(await _history.GetRecentAsync(-5));
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsEmpty_WhenNoScores()
        => Assert.Empty(await _history.GetRecentAsync(10));

    [Fact]
    public async Task GetForSongAsync_FiltersByNumber_AndOrdersMostRecentFirst()
    {
        await _history.AddAsync(NewRecord("SONG-1", 50.0, BaseTime.AddMinutes(1)));
        await _history.AddAsync(NewRecord("SONG-2", 60.0, BaseTime.AddMinutes(2)));
        await _history.AddAsync(NewRecord("SONG-1", 90.0, BaseTime.AddMinutes(3)));

        var forSong1 = await _history.GetForSongAsync("SONG-1");
        Assert.Equal(2, forSong1.Count);
        // Most recent first: the 90.0 entry (minute 3) precedes the 50.0 entry (minute 1).
        Assert.Equal(new[] { 90.0, 50.0 }, forSong1.Select(r => r.Overall));
        Assert.All(forSong1, r => Assert.Equal("SONG-1", r.SongNumber));

        Assert.Empty(await _history.GetForSongAsync("NO-SUCH"));
    }

    [Fact]
    public async Task GetBestAsync_ReturnsHighestOverall()
    {
        await _history.AddAsync(NewRecord("HIT", 71.0, BaseTime.AddMinutes(1)));
        await _history.AddAsync(NewRecord("HIT", 95.0, BaseTime.AddMinutes(2)));
        await _history.AddAsync(NewRecord("HIT", 83.0, BaseTime.AddMinutes(3)));
        // A different song's high score must not leak in.
        await _history.AddAsync(NewRecord("OTHER", 99.0, BaseTime.AddMinutes(4)));

        var best = await _history.GetBestAsync("HIT");
        Assert.NotNull(best);
        Assert.Equal(95.0, best!.Overall);
        Assert.Equal("HIT", best.SongNumber);
    }

    [Fact]
    public async Task GetBestAsync_ReturnsNull_WhenSongHasNoScores()
        => Assert.Null(await _history.GetBestAsync("NEVER-SUNG"));

    [Fact]
    public async Task ScoredAt_RoundTripsExactly_WithNonUtcOffset()
    {
        // A non-UTC offset with sub-second precision must survive storage and retrieval.
        var scoredAt = new DateTimeOffset(2026, 3, 14, 9, 26, 53, 589, TimeSpan.FromHours(-5))
            .AddTicks(7931);

        var stored = await _history.AddAsync(NewRecord("PI", 62.0, scoredAt));

        // The returned record (from RETURNING) round-trips...
        Assert.Equal(scoredAt, stored.ScoredAt);
        Assert.Equal(scoredAt.Offset, stored.ScoredAt.Offset);

        // ...and so does the value re-read from a fresh query.
        var fetched = Assert.Single(await _history.GetForSongAsync("PI"));
        Assert.Equal(scoredAt, fetched.ScoredAt);
        Assert.Equal(scoredAt.Offset, fetched.ScoredAt.Offset);
        Assert.Equal(scoredAt.UtcTicks, fetched.ScoredAt.UtcTicks);
    }

    [Fact]
    public void FromScoreResult_MapsAllFields()
    {
        var result = new ScoreResult
        {
            Overall = 84.0, // Grade => "A"
            PitchAccuracy = 90.0,
            Stability = 80.0,
            Expression = 70.0,
            LongTone = 60.0,
            VibratoCount = 4,
            ShakuriCount = 5,
            KobushiCount = 6,
        };
        var scoredAt = new DateTimeOffset(2026, 6, 21, 20, 0, 0, TimeSpan.FromHours(9));

        var record = ScoreRecord.FromScoreResult(
            result,
            songNumber: "7777-77",
            songTitle: "Pretender",
            songArtist: "Official HIGE DANdism",
            scoredAt: scoredAt,
            keyOffset: -3,
            tempoPercent: 95.0);

        Assert.Equal("7777-77", record.SongNumber);
        Assert.Equal("Pretender", record.SongTitle);
        Assert.Equal("Official HIGE DANdism", record.SongArtist);
        Assert.Equal(scoredAt, record.ScoredAt);
        Assert.Equal(84.0, record.Overall);
        Assert.Equal(90.0, record.PitchAccuracy);
        Assert.Equal(80.0, record.Stability);
        Assert.Equal(70.0, record.Expression);
        Assert.Equal(60.0, record.LongTone);
        Assert.Equal(4, record.VibratoCount);
        Assert.Equal(5, record.ShakuriCount);
        Assert.Equal(6, record.KobushiCount);
        Assert.Equal("A", record.Grade); // taken from ScoreResult.Grade
        Assert.Equal(-3, record.KeyOffset);
        Assert.Equal(95.0, record.TempoPercent);
    }

    [Fact]
    public async Task FromScoreResult_RecordRoundTripsThroughStore()
    {
        var result = new ScoreResult
        {
            Overall = 77.5,
            PitchAccuracy = 78.0,
            Stability = 77.0,
            Expression = 76.0,
            LongTone = 79.0,
            VibratoCount = 2,
            ShakuriCount = 1,
            KobushiCount = 0,
        };
        var scoredAt = new DateTimeOffset(2026, 6, 21, 21, 30, 0, TimeSpan.FromHours(9));
        var record = ScoreRecord.FromScoreResult(
            result, "9001", "Marigold", "Aimyon", scoredAt, keyOffset: 2, tempoPercent: 110.0);

        var stored = await _history.AddAsync(record);
        var fetched = Assert.Single(await _history.GetForSongAsync("9001"));

        Assert.Equal(stored.Id, fetched.Id);
        Assert.Equal(77.5, fetched.Overall);
        Assert.Equal("B", fetched.Grade); // 77.5 => B
        Assert.Equal(2, fetched.KeyOffset);
        Assert.Equal(110.0, fetched.TempoPercent);
        Assert.Equal(scoredAt, fetched.ScoredAt);
    }
}
