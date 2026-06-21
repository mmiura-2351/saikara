using Saikara.Core.Editor;

namespace Saikara.Core.Tests.Editor;

/// <summary>Tests for <see cref="SqliteSongCorrectionsStore"/>.</summary>
public sealed class SqliteSongCorrectionsStoreTests : IAsyncDisposable
{
    private readonly SqliteSongCorrectionsStore _store = new("Data Source=:memory:");

    public async ValueTask DisposeAsync()
    {
        await _store.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_Can_Be_Called_Twice_Idempotent()
    {
        await _store.InitializeAsync();
        await _store.InitializeAsync(); // should not throw
    }

    [Fact]
    public async Task GetAsync_Returns_Null_When_No_Corrections_Exist()
    {
        await _store.InitializeAsync();
        var result = await _store.GetAsync("SONG-001");
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_Then_GetAsync_Returns_Corrections()
    {
        await _store.InitializeAsync();

        var corrections = new SongCorrections
        {
            MelodyTrackIndex = 3,
            LyricOffsetMs = 100.5,
        };

        await _store.SaveAsync("SONG-002", corrections);
        var result = await _store.GetAsync("SONG-002");

        Assert.NotNull(result);
        Assert.Equal(3, result.MelodyTrackIndex);
        Assert.Equal(100.5, result.LyricOffsetMs);
        Assert.Null(result.PerSyllableAdjustments);
    }

    [Fact]
    public async Task SaveAsync_With_PerSyllableAdjustments_Roundtrips()
    {
        await _store.InitializeAsync();

        var corrections = new SongCorrections
        {
            MelodyTrackIndex = 1,
            LyricOffsetMs = -50.0,
            PerSyllableAdjustments = new[]
            {
                new SyllableAdjustment(0, 10.0),
                new SyllableAdjustment(3, -25.5),
            },
        };

        await _store.SaveAsync("SONG-003", corrections);
        var result = await _store.GetAsync("SONG-003");

        Assert.NotNull(result);
        Assert.Equal(1, result.MelodyTrackIndex);
        Assert.Equal(-50.0, result.LyricOffsetMs);
        Assert.NotNull(result.PerSyllableAdjustments);
        Assert.Equal(2, result.PerSyllableAdjustments.Count);
        Assert.Equal(0, result.PerSyllableAdjustments[0].LyricIndex);
        Assert.Equal(10.0, result.PerSyllableAdjustments[0].DeltaMs);
        Assert.Equal(3, result.PerSyllableAdjustments[1].LyricIndex);
        Assert.Equal(-25.5, result.PerSyllableAdjustments[1].DeltaMs);
    }

    [Fact]
    public async Task SaveAsync_Overwrites_Previous_Corrections()
    {
        await _store.InitializeAsync();

        var first = new SongCorrections
        {
            MelodyTrackIndex = 1,
            LyricOffsetMs = 100.0,
        };
        await _store.SaveAsync("SONG-004", first);

        var second = new SongCorrections
        {
            MelodyTrackIndex = 5,
            LyricOffsetMs = -200.0,
        };
        await _store.SaveAsync("SONG-004", second);

        var result = await _store.GetAsync("SONG-004");
        Assert.NotNull(result);
        Assert.Equal(5, result.MelodyTrackIndex);
        Assert.Equal(-200.0, result.LyricOffsetMs);
    }

    [Fact]
    public async Task SaveAsync_With_Null_MelodyTrackIndex_Persists_Null()
    {
        await _store.InitializeAsync();

        var corrections = new SongCorrections
        {
            MelodyTrackIndex = null,
            LyricOffsetMs = 50.0,
        };

        await _store.SaveAsync("SONG-005", corrections);
        var result = await _store.GetAsync("SONG-005");

        Assert.NotNull(result);
        Assert.Null(result.MelodyTrackIndex);
        Assert.Equal(50.0, result.LyricOffsetMs);
    }

    [Fact]
    public async Task DeleteAsync_Removes_Corrections()
    {
        await _store.InitializeAsync();

        var corrections = new SongCorrections { MelodyTrackIndex = 2 };
        await _store.SaveAsync("SONG-006", corrections);

        await _store.DeleteAsync("SONG-006");
        var result = await _store.GetAsync("SONG-006");

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_Is_Noop_When_None_Exist()
    {
        await _store.InitializeAsync();
        await _store.DeleteAsync("NONEXISTENT"); // should not throw
    }

    [Fact]
    public async Task Different_SongNumbers_Are_Independent()
    {
        await _store.InitializeAsync();

        var c1 = new SongCorrections { MelodyTrackIndex = 1, LyricOffsetMs = 10.0 };
        var c2 = new SongCorrections { MelodyTrackIndex = 2, LyricOffsetMs = 20.0 };

        await _store.SaveAsync("SONG-A", c1);
        await _store.SaveAsync("SONG-B", c2);

        var r1 = await _store.GetAsync("SONG-A");
        var r2 = await _store.GetAsync("SONG-B");

        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal(1, r1.MelodyTrackIndex);
        Assert.Equal(2, r2.MelodyTrackIndex);
    }

    [Fact]
    public async Task SaveAsync_ZeroOffset_NoAdjustments_Roundtrips()
    {
        await _store.InitializeAsync();

        var corrections = new SongCorrections
        {
            MelodyTrackIndex = null,
            LyricOffsetMs = 0.0,
            PerSyllableAdjustments = null,
        };

        await _store.SaveAsync("SONG-007", corrections);
        var result = await _store.GetAsync("SONG-007");

        Assert.NotNull(result);
        Assert.Null(result.MelodyTrackIndex);
        Assert.Equal(0.0, result.LyricOffsetMs);
        Assert.Null(result.PerSyllableAdjustments);
    }

    [Fact]
    public void Constructor_Throws_On_Empty_String()
    {
        Assert.Throws<ArgumentException>(() => new SqliteSongCorrectionsStore(""));
    }

    [Fact]
    public void Constructor_Throws_On_Whitespace()
    {
        Assert.Throws<ArgumentException>(() => new SqliteSongCorrectionsStore("   "));
    }

    [Fact]
    public async Task SaveAsync_EmptyList_PerSyllableAdjustments_Roundtrips_AsNull()
    {
        await _store.InitializeAsync();

        var corrections = new SongCorrections
        {
            PerSyllableAdjustments = Array.Empty<SyllableAdjustment>(),
        };

        await _store.SaveAsync("SONG-008", corrections);
        var result = await _store.GetAsync("SONG-008");

        Assert.NotNull(result);
        // An empty list is serialized as null (no adjustments) for storage efficiency.
        Assert.Null(result.PerSyllableAdjustments);
    }
}
