using Saikara.Core.Library;

namespace Saikara.Core.Tests.Library;

/// <summary>
/// Tests for <see cref="SqliteSongLibrary"/> against a real SQLite database.
/// Each test gets its own unique shared-cache in-memory database; the library keeps a
/// single connection open, so the schema and data survive across calls for the lifetime
/// of the test, and the database is discarded when the library is disposed.
/// </summary>
public sealed class SqliteSongLibraryTests : IAsyncLifetime, IDisposable
{
    private static int _counter;
    private readonly SqliteSongLibrary _library;

    public SqliteSongLibraryTests()
    {
        // A unique name per test keeps the shared-cache in-memory databases isolated.
        int id = Interlocked.Increment(ref _counter);
        _library = new SqliteSongLibrary($"Data Source=test-{id};Mode=Memory;Cache=Shared");
    }

    public Task InitializeAsync() => _library.InitializeAsync();

    public Task DisposeAsync()
    {
        _library.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _library.Dispose();

    private static Song NewSong(
        string number,
        string title = "Some Title",
        string artist = "Some Artist",
        string filePath = "C:/songs/some.kar",
        int? melodyTrackIndex = null)
        => new()
        {
            Number = number,
            Title = title,
            Artist = artist,
            FilePath = filePath,
            MelodyTrackIndex = melodyTrackIndex,
            DateAdded = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.FromHours(9)),
        };

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        // Already initialized once in InitializeAsync(); calling again must not throw
        // and must not disturb existing data.
        await _library.UpsertAsync(NewSong("1000-01"));

        await _library.InitializeAsync();
        await _library.InitializeAsync();

        Assert.Single(await _library.GetAllAsync());
    }

    [Fact]
    public async Task UpsertAsync_InsertsAndAssignsId_AndGetByIdRoundTrips()
    {
        var stored = await _library.UpsertAsync(
            NewSong("1234-56", title: "Sakura", artist: "Naotaro Moriyama", melodyTrackIndex: 2));

        Assert.True(stored.Id > 0);

        var fetched = await _library.GetByIdAsync(stored.Id);
        Assert.NotNull(fetched);
        Assert.Equal(stored.Id, fetched!.Id);
        Assert.Equal("1234-56", fetched.Number);
        Assert.Equal("Sakura", fetched.Title);
        Assert.Equal("Naotaro Moriyama", fetched.Artist);
        Assert.Equal("C:/songs/some.kar", fetched.FilePath);
        Assert.Equal(2, fetched.MelodyTrackIndex);
        Assert.Equal(stored.DateAdded, fetched.DateAdded);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
        => Assert.Null(await _library.GetByIdAsync(9999));

    [Fact]
    public async Task UpsertAsync_PreservesNullMelodyTrackIndex()
    {
        var stored = await _library.UpsertAsync(NewSong("0001", melodyTrackIndex: null));
        Assert.Null(stored.MelodyTrackIndex);

        var fetched = await _library.GetByIdAsync(stored.Id);
        Assert.Null(fetched!.MelodyTrackIndex);
    }

    [Fact]
    public async Task GetByNumberAsync_FindsByKaraokeNumber()
    {
        await _library.UpsertAsync(NewSong("AAAA-11"));
        await _library.UpsertAsync(NewSong("BBBB-22"));

        var fetched = await _library.GetByNumberAsync("BBBB-22");
        Assert.NotNull(fetched);
        Assert.Equal("BBBB-22", fetched!.Number);

        Assert.Null(await _library.GetByNumberAsync("NOPE-00"));
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingNumber_WithoutDuplicatingRow()
    {
        var first = await _library.UpsertAsync(
            NewSong("5555-55", title: "Old Title", artist: "Old Artist", melodyTrackIndex: 1));

        var updated = await _library.UpsertAsync(
            NewSong("5555-55", title: "New Title", artist: "New Artist",
                filePath: "C:/songs/new.kar", melodyTrackIndex: 3));

        // Same logical row: id preserved, no second row created.
        Assert.Equal(first.Id, updated.Id);
        Assert.Single(await _library.GetAllAsync());

        var fetched = await _library.GetByNumberAsync("5555-55");
        Assert.Equal("New Title", fetched!.Title);
        Assert.Equal("New Artist", fetched.Artist);
        Assert.Equal("C:/songs/new.kar", fetched.FilePath);
        Assert.Equal(3, fetched.MelodyTrackIndex);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEverything_OrderedByNumber()
    {
        await _library.UpsertAsync(NewSong("3000"));
        await _library.UpsertAsync(NewSong("1000"));
        await _library.UpsertAsync(NewSong("2000"));

        var all = await _library.GetAllAsync();
        Assert.Equal(new[] { "1000", "2000", "3000" }, all.Select(s => s.Number));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmpty_WhenLibraryEmpty()
        => Assert.Empty(await _library.GetAllAsync());

    private async Task SeedSearchCorpusAsync()
    {
        await _library.UpsertAsync(NewSong("1111-AA", title: "Lemon", artist: "Kenshi Yonezu"));
        await _library.UpsertAsync(NewSong("2222-BB", title: "Pretender", artist: "Official HIGE DANdism"));
        await _library.UpsertAsync(NewSong("3333-CC", title: "Marigold", artist: "Aimyon"));
    }

    [Fact]
    public async Task SearchAsync_MatchesByNumber()
    {
        await SeedSearchCorpusAsync();
        var results = await _library.SearchAsync("2222");
        Assert.Equal(new[] { "2222-BB" }, results.Select(s => s.Number));
    }

    [Fact]
    public async Task SearchAsync_MatchesByTitleSubstring()
    {
        await SeedSearchCorpusAsync();
        var results = await _library.SearchAsync("mon"); // substring of "Lemon"
        Assert.Equal(new[] { "1111-AA" }, results.Select(s => s.Number));
    }

    [Fact]
    public async Task SearchAsync_MatchesByArtistSubstring()
    {
        await SeedSearchCorpusAsync();
        var results = await _library.SearchAsync("aimyon");
        Assert.Equal(new[] { "3333-CC" }, results.Select(s => s.Number));
    }

    [Fact]
    public async Task SearchAsync_IsCaseInsensitive()
    {
        await SeedSearchCorpusAsync();
        var lower = await _library.SearchAsync("lemon");
        var upper = await _library.SearchAsync("LEMON");
        var mixed = await _library.SearchAsync("LeMoN");

        Assert.Equal(new[] { "1111-AA" }, lower.Select(s => s.Number));
        Assert.Equal(new[] { "1111-AA" }, upper.Select(s => s.Number));
        Assert.Equal(new[] { "1111-AA" }, mixed.Select(s => s.Number));
    }

    [Fact]
    public async Task SearchAsync_ReturnsAll_OnEmptyOrWhitespaceQuery()
    {
        await SeedSearchCorpusAsync();

        Assert.Equal(3, (await _library.SearchAsync("")).Count);
        Assert.Equal(3, (await _library.SearchAsync("   ")).Count);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenNoMatch()
    {
        await SeedSearchCorpusAsync();
        Assert.Empty(await _library.SearchAsync("no-such-song"));
    }

    [Fact]
    public async Task SearchAsync_CanMatchMultipleSongs()
    {
        await _library.UpsertAsync(NewSong("A1", title: "Love Song", artist: "Artist X"));
        await _library.UpsertAsync(NewSong("A2", title: "Another", artist: "Love Handles"));
        await _library.UpsertAsync(NewSong("A3", title: "Nope", artist: "Nope"));

        var results = await _library.SearchAsync("love");
        Assert.Equal(new[] { "A1", "A2" }, results.Select(s => s.Number));
    }
}
