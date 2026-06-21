using System.Net;
using Saikara.Core.Audio;

namespace Saikara.Core.Tests.Audio;

public class SoundFontInstallerTests : IDisposable
{
    private readonly string _baseDir;

    public SoundFontInstallerTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), $"saikara-sf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDir))
        {
            Directory.Delete(_baseDir, recursive: true);
        }
    }

    // --- Path resolution ---

    [Fact]
    public void DefaultSoundFontPath_IsUnderLocalAppDataSaikaraSoundfonts()
    {
        var installer = new SoundFontInstaller(new HttpClient(), baseDirectory: _baseDir);

        string expected = Path.Combine(
            _baseDir, "Saikara", "soundfonts", SoundFontInstaller.DefaultSoundFontFileName);

        Assert.Equal(expected, installer.DefaultSoundFontPath);
    }

    [Fact]
    public void DefaultSoundFontPath_DefaultsToLocalApplicationData()
    {
        var installer = new SoundFontInstaller(new HttpClient());

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string expected = Path.Combine(
            localAppData, "Saikara", "soundfonts", SoundFontInstaller.DefaultSoundFontFileName);

        Assert.Equal(expected, installer.DefaultSoundFontPath);
    }

    // --- Download when absent ---

    [Fact]
    public async Task EnsureDefaultSoundFontAsync_DownloadsWhenAbsent()
    {
        byte[] payload = RandomBytes(2048);
        var handler = new FakeHandler(payload);
        var installer = new SoundFontInstaller(new HttpClient(handler), baseDirectory: _baseDir);

        string path = await installer.EnsureDefaultSoundFontAsync();

        Assert.Equal(installer.DefaultSoundFontPath, path);
        Assert.True(File.Exists(path));
        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task EnsureDefaultSoundFontAsync_CreatesParentDirectories()
    {
        var handler = new FakeHandler(RandomBytes(16));
        var installer = new SoundFontInstaller(new HttpClient(handler), baseDirectory: _baseDir);

        await installer.EnsureDefaultSoundFontAsync();

        Assert.True(Directory.Exists(Path.GetDirectoryName(installer.DefaultSoundFontPath)));
    }

    [Fact]
    public async Task EnsureDefaultSoundFontAsync_ReportsProgress()
    {
        var handler = new FakeHandler(RandomBytes(4096));
        var installer = new SoundFontInstaller(new HttpClient(handler), baseDirectory: _baseDir);

        // Progress<T> posts asynchronously; collect synchronously via a custom IProgress.
        var sync = new SyncProgress();

        await installer.EnsureDefaultSoundFontAsync(progress: sync);

        Assert.NotEmpty(sync.Values);
        Assert.All(sync.Values, p => Assert.InRange(p, 0.0, 1.0));
        Assert.Equal(1.0, sync.Values[^1], precision: 3);
    }

    // --- Idempotency ---

    [Fact]
    public async Task EnsureDefaultSoundFontAsync_Idempotent_DoesNotRedownload()
    {
        byte[] payload = RandomBytes(1024);
        var handler = new FakeHandler(payload);
        var installer = new SoundFontInstaller(new HttpClient(handler), baseDirectory: _baseDir);

        string first = await installer.EnsureDefaultSoundFontAsync();
        Assert.Equal(1, handler.CallCount);

        string second = await installer.EnsureDefaultSoundFontAsync();

        Assert.Equal(first, second);
        // Handler must not be invoked a second time.
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(payload, await File.ReadAllBytesAsync(second));
    }

    // --- Failure handling ---

    [Fact]
    public async Task EnsureDefaultSoundFontAsync_OnThrowingHandler_SurfacesErrorAndLeavesNoFile()
    {
        var handler = new FakeHandler(new HttpRequestException("network down"));
        var installer = new SoundFontInstaller(new HttpClient(handler), baseDirectory: _baseDir);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => installer.EnsureDefaultSoundFontAsync());

        AssertNoArtifacts(installer.DefaultSoundFontPath);
    }

    [Fact]
    public async Task EnsureDefaultSoundFontAsync_OnNonSuccessStatus_SurfacesErrorAndLeavesNoFile()
    {
        var handler = new FakeHandler(HttpStatusCode.NotFound);
        var installer = new SoundFontInstaller(new HttpClient(handler), baseDirectory: _baseDir);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => installer.EnsureDefaultSoundFontAsync());

        AssertNoArtifacts(installer.DefaultSoundFontPath);
    }

    [Fact]
    public async Task EnsureDefaultSoundFontAsync_AfterFailure_CanSucceedOnRetry()
    {
        var failing = new SoundFontInstaller(
            new HttpClient(new FakeHandler(new HttpRequestException("boom"))),
            baseDirectory: _baseDir);
        await Assert.ThrowsAsync<HttpRequestException>(() => failing.EnsureDefaultSoundFontAsync());

        byte[] payload = RandomBytes(512);
        var ok = new SoundFontInstaller(
            new HttpClient(new FakeHandler(payload)), baseDirectory: _baseDir);

        string path = await ok.EnsureDefaultSoundFontAsync();

        Assert.True(File.Exists(path));
        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        // No leftover temp file alongside the final .sf2.
        AssertNoTempFiles(path);
    }

    // --- Custom URL ---

    [Fact]
    public async Task EnsureDefaultSoundFontAsync_UsesConfiguredUrl()
    {
        var handler = new FakeHandler(RandomBytes(8));
        var installer = new SoundFontInstaller(
            new HttpClient(handler),
            baseDirectory: _baseDir,
            downloadUrl: "https://example.invalid/custom.sf2");

        await installer.EnsureDefaultSoundFontAsync();

        Assert.Equal("https://example.invalid/custom.sf2", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public void DefaultDownloadUrl_IsConfiguredConstant()
    {
        Assert.False(string.IsNullOrWhiteSpace(SoundFontInstaller.DefaultDownloadUrl));
        Assert.StartsWith("https://", SoundFontInstaller.DefaultDownloadUrl);
    }

    [Fact]
    public void Constructor_NullHttpClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SoundFontInstaller(null!));
    }

    // --- Helpers ---

    private void AssertNoArtifacts(string finalPath)
    {
        Assert.False(File.Exists(finalPath), "final .sf2 must not exist after a failed download");
        AssertNoTempFiles(finalPath);
    }

    private void AssertNoTempFiles(string finalPath)
    {
        string dir = Path.GetDirectoryName(finalPath)!;
        if (!Directory.Exists(dir))
        {
            return;
        }

        var leftovers = Directory.EnumerateFiles(dir)
            .Where(f => !string.Equals(f, finalPath, StringComparison.Ordinal))
            .ToList();
        Assert.Empty(leftovers);
    }

    private static byte[] RandomBytes(int count)
    {
        var bytes = new byte[count];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    /// <summary>A synchronous <see cref="IProgress{T}"/> so tests can assert reports deterministically.</summary>
    private sealed class SyncProgress : IProgress<double>
    {
        public List<double> Values { get; } = new();

        public void Report(double value) => Values.Add(value);
    }

    /// <summary>
    /// A fake <see cref="HttpMessageHandler"/> that returns fixed bytes, a status code, or
    /// throws — so the installer is testable without touching the network.
    /// </summary>
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

        public FakeHandler(HttpStatusCode status)
        {
            _status = status;
        }

        public FakeHandler(Exception exception)
        {
            _exception = exception;
        }

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
