using System.Net;
using Saikara.Core.Import;
using Saikara.Core.Library;
using Saikara.Core.Midi;
using Saikara.Core.Tests.Midi;

namespace Saikara.Core.Tests.Import;

/// <summary>
/// Tests for <see cref="MidiImportService"/> against a real in-memory <see cref="SqliteSongLibrary"/>
/// (shared-cache, kept open) and a fake <see cref="HttpMessageHandler"/> — no real network and no
/// shared global state. Each test owns a unique temp library directory and cleans it up.
/// </summary>
public sealed class MidiImportServiceTests : IAsyncLifetime, IDisposable
{
    private const short Tpqn = 480;
    private static int _counter;

    private readonly SqliteSongLibrary _library;
    private readonly string _libraryDir;
    private readonly string _sourceDir;

    private static readonly DateTimeOffset AddedAt =
        new(2026, 6, 21, 12, 0, 0, TimeSpan.FromHours(9));

    public MidiImportServiceTests()
    {
        int id = Interlocked.Increment(ref _counter);
        _library = new SqliteSongLibrary($"Data Source=import-test-{id};Mode=Memory;Cache=Shared");

        string root = Path.Combine(Path.GetTempPath(), $"saikara-import-test-{Guid.NewGuid():N}");
        _libraryDir = Path.Combine(root, "library");
        _sourceDir = Path.Combine(root, "source");
        Directory.CreateDirectory(_sourceDir);
    }

    public Task InitializeAsync() => _library.InitializeAsync();

    public Task DisposeAsync()
    {
        _library.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _library.Dispose();
        string root = Path.GetDirectoryName(_libraryDir)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- Helpers ---

    private MidiImportService NewService(HttpMessageHandler? handler = null)
        => new(_library, new MidiLoader(), new HttpClient(handler ?? new FakeHandler(Array.Empty<byte>())), _libraryDir);

    private static byte[] BuildMidi(string? title = null, string? artist = null, bool withMelody = false, bool withLyrics = false)
    {
        var meta = new List<Melanchall.DryWetMidi.Interaction.TimedEvent>();
        if (title is not null)
        {
            meta.Add(TestMidiBuilder.Text("@T " + title, 0));
        }
        if (artist is not null)
        {
            meta.Add(TestMidiBuilder.Text("@A " + artist, 0));
        }
        if (withLyrics)
        {
            meta.Add(TestMidiBuilder.Lyric("la", Tpqn));
        }

        var builder = new TestMidiBuilder(Tpqn).AddTrack(meta);

        var notes = TestMidiBuilder.Note(60, 0, 0, Tpqn);
        if (withMelody)
        {
            notes = notes.Prepend(TestMidiBuilder.TrackName("Melody"));
        }
        builder.AddTrack(notes);

        using var stream = builder.BuildStream();
        return stream.ToArray();
    }

    private string WriteSource(byte[] bytes, string fileName)
    {
        string path = Path.Combine(_sourceDir, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // --- ImportFileAsync ---

    [Fact]
    public async Task ImportFileAsync_AddsSong_AndCopiesFileIntoLibrary()
    {
        byte[] bytes = BuildMidi(title: "Sakura", artist: "Moriyama", withMelody: true, withLyrics: true);
        string source = WriteSource(bytes, "sakura.kar");
        var service = NewService();

        ImportResult result = await service.ImportFileAsync(source, AddedAt);

        // A song was stored.
        Song stored = result.Song;
        Assert.True(stored.Id > 0);
        Assert.Equal("Sakura", stored.Title);
        Assert.Equal("Moriyama", stored.Artist);
        Assert.NotNull(stored.MelodyTrackIndex);
        Assert.Equal(stored.MelodyTrackIndex, result.MelodyTrackIndex);
        Assert.True(result.HasLyrics);
        Assert.True(result.Duration > TimeSpan.Zero);

        // The file was copied into the library directory and exists.
        Assert.True(File.Exists(stored.FilePath));
        Assert.Equal(
            Path.GetFullPath(_libraryDir),
            Path.GetFullPath(Path.GetDirectoryName(stored.FilePath)!));
        Assert.Equal(".kar", Path.GetExtension(stored.FilePath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(stored.FilePath));

        // It is discoverable in the library.
        Assert.Single(await _library.GetAllAsync());

        // The source was copied, not moved.
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task ImportFileAsync_KeepsMidExtension()
    {
        string source = WriteSource(BuildMidi(title: "Song"), "song.mid");
        var service = NewService();

        ImportResult result = await service.ImportFileAsync(source, AddedAt);

        Assert.Equal(".mid", Path.GetExtension(result.Song.FilePath));
    }

    [Fact]
    public async Task ImportFileAsync_FallsBackToFileName_WhenNoMetadata()
    {
        string source = WriteSource(BuildMidi(), "Cool Track.mid");
        var service = NewService();

        ImportResult result = await service.ImportFileAsync(source, AddedAt);

        Assert.Equal("Cool Track", result.Song.Title);
    }

    [Fact]
    public async Task ImportFileAsync_SetsDateAddedFromCaller()
    {
        string source = WriteSource(BuildMidi(title: "T"), "t.mid");
        var service = NewService();

        ImportResult result = await service.ImportFileAsync(source, AddedAt);

        Assert.Equal(AddedAt, result.Song.DateAdded);
    }

    [Fact]
    public async Task ImportFileAsync_NoMelody_LeavesMelodyTrackIndexNull()
    {
        string source = WriteSource(BuildMidi(title: "T"), "t.mid");
        var service = NewService();

        ImportResult result = await service.ImportFileAsync(source, AddedAt);

        Assert.Null(result.Song.MelodyTrackIndex);
        Assert.Null(result.MelodyTrackIndex);
    }

    // --- Validation / rejection ---

    [Fact]
    public async Task ImportFileAsync_RejectsGarbage_AddsNoSong_LeavesNoFile()
    {
        byte[] garbage = new byte[256];
        Random.Shared.NextBytes(garbage);
        string source = WriteSource(garbage, "not-a-midi.kar");
        var service = NewService();

        await Assert.ThrowsAsync<ImportException>(() => service.ImportFileAsync(source, AddedAt));

        Assert.Empty(await _library.GetAllAsync());
        AssertLibraryEmpty();
    }

    [Fact]
    public async Task ImportFileAsync_MissingFile_Throws_AndAddsNoSong()
    {
        var service = NewService();
        string missing = Path.Combine(_sourceDir, "does-not-exist.mid");

        await Assert.ThrowsAsync<ImportException>(() => service.ImportFileAsync(missing, AddedAt));

        Assert.Empty(await _library.GetAllAsync());
    }

    // --- ImportUrlAsync ---

    [Fact]
    public async Task ImportUrlAsync_DownloadsAndImports()
    {
        byte[] bytes = BuildMidi(title: "Net Song", artist: "Net Artist", withLyrics: true);
        var handler = new FakeHandler(bytes);
        var service = NewService(handler);

        ImportResult result = await service.ImportUrlAsync(
            "https://example.invalid/songs/netsong.kar", addedAt: AddedAt);

        Assert.Equal("Net Song", result.Song.Title);
        Assert.Equal("Net Artist", result.Song.Artist);
        Assert.True(File.Exists(result.Song.FilePath));
        Assert.Equal(".kar", Path.GetExtension(result.Song.FilePath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result.Song.FilePath));
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("https://example.invalid/songs/netsong.kar", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ImportUrlAsync_UsesSuggestedName_ForFileName()
    {
        byte[] bytes = BuildMidi();
        var service = NewService(new FakeHandler(bytes));

        ImportResult result = await service.ImportUrlAsync(
            "https://example.invalid/download?id=42", suggestedName: "Pretty Name.mid", addedAt: AddedAt);

        Assert.Equal("Pretty Name", Path.GetFileNameWithoutExtension(result.Song.FilePath));
        Assert.Equal("Pretty Name", result.Song.Title);
    }

    [Fact]
    public async Task ImportUrlAsync_RejectsGarbage_AddsNoSong_LeavesNoFile()
    {
        byte[] garbage = new byte[128];
        Random.Shared.NextBytes(garbage);
        var service = NewService(new FakeHandler(garbage));

        await Assert.ThrowsAsync<ImportException>(
            () => service.ImportUrlAsync("https://example.invalid/bad.kar", addedAt: AddedAt));

        Assert.Empty(await _library.GetAllAsync());
        AssertLibraryEmpty();
    }

    [Fact]
    public async Task ImportUrlAsync_OnHttpError_Throws_AddsNoSong_LeavesNoFile()
    {
        var service = NewService(new FakeHandler(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<ImportException>(
            () => service.ImportUrlAsync("https://example.invalid/missing.mid", addedAt: AddedAt));

        Assert.Empty(await _library.GetAllAsync());
        AssertLibraryEmpty();
    }

    // --- Re-import / dedup ---

    [Fact]
    public async Task ImportFileAsync_ReimportSameBytes_UpdatesInPlace_NoDuplicate()
    {
        byte[] bytes = BuildMidi(title: "Same Song", artist: "Same Artist");
        string first = WriteSource(bytes, "first.mid");
        string second = WriteSource(bytes, "second.mid");
        var service = NewService();

        ImportResult a = await service.ImportFileAsync(first, AddedAt);
        ImportResult b = await service.ImportFileAsync(second, AddedAt);

        // Same content => same generated Number => same row.
        Assert.Equal(a.Song.Number, b.Song.Number);
        Assert.Equal(a.Song.Id, b.Song.Id);
        Assert.Single(await _library.GetAllAsync());
    }

    [Fact]
    public async Task ImportFileAsync_DifferentSongs_GetDistinctNumbers()
    {
        string s1 = WriteSource(BuildMidi(title: "One"), "one.mid");
        string s2 = WriteSource(BuildMidi(title: "Two"), "two.mid");
        var service = NewService();

        ImportResult r1 = await service.ImportFileAsync(s1, AddedAt);
        ImportResult r2 = await service.ImportFileAsync(s2, AddedAt);

        Assert.NotEqual(r1.Song.Number, r2.Song.Number);
        Assert.Equal(2, (await _library.GetAllAsync()).Count);
    }

    [Fact]
    public async Task GeneratedNumber_HasImportPrefix()
    {
        string source = WriteSource(BuildMidi(title: "T"), "t.mid");
        var service = NewService();

        ImportResult result = await service.ImportFileAsync(source, AddedAt);

        Assert.StartsWith("IMP-", result.Song.Number);
    }

    // --- Constructor guards ---

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => new MidiImportService(null!, new MidiLoader(), new HttpClient(), _libraryDir));
        Assert.Throws<ArgumentNullException>(
            () => new MidiImportService(_library, null!, new HttpClient(), _libraryDir));
        Assert.Throws<ArgumentNullException>(
            () => new MidiImportService(_library, new MidiLoader(), null!, _libraryDir));
    }

    // --- Assertions ---

    private void AssertLibraryEmpty()
    {
        if (!Directory.Exists(_libraryDir))
        {
            return;
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(_libraryDir));
    }

    /// <summary>A fake handler returning fixed bytes, a status code, or throwing.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly byte[]? _payload;
        private readonly HttpStatusCode _status;
        private readonly Exception? _exception;

        public int CallCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        public FakeHandler(byte[] payload)
        {
            _payload = payload;
            _status = HttpStatusCode.OK;
        }

        public FakeHandler(HttpStatusCode status) => _status = status;

        public FakeHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;

            if (_exception is not null)
            {
                return Task.FromException<HttpResponseMessage>(_exception);
            }

            var response = new HttpResponseMessage(_status);
            if (_payload is not null)
            {
                response.Content = new ByteArrayContent(_payload);
            }

            return Task.FromResult(response);
        }
    }
}
