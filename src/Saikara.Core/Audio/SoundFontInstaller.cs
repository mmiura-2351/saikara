namespace Saikara.Core.Audio;

/// <summary>
/// Resolves the default SoundFont path and, on first run, auto-downloads a free default
/// SoundFont so the SoundFont-based synth (MeltySynth in <c>Saikara.App</c>) has a voice bank
/// without the user having to supply one. The download is atomic and idempotent: it streams
/// into a temp file in the destination directory and renames it into place only on success,
/// and does nothing if the target already exists.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> is injected so the download is fully testable without real
/// network access (tests supply a fake <see cref="HttpMessageHandler"/>). This type does no
/// audio I/O or synthesis — it only manages the SoundFont asset file.
/// </remarks>
public sealed class SoundFontInstaller
{
    /// <summary>
    /// File name of the bundled default SoundFont under the soundfonts directory.
    /// </summary>
    public const string DefaultSoundFontFileName = "GeneralUserGS.sf2";

    /// <summary>
    /// Default download URL for the free, freely-redistributable GeneralUser GS SoundFont.
    /// </summary>
    /// <remarks>
    /// This is <b>user-configurable</b>: pass a different URL to the constructor (or expose it
    /// in settings) to install another freely-redistributable SoundFont. GeneralUser GS by
    /// S. Christian Collins is distributed under a permissive license that allows
    /// redistribution; verify the license of any substitute. The URL is only contacted when
    /// the target file is absent.
    /// </remarks>
    public const string DefaultDownloadUrl =
        "https://github.com/mrbumpy409/GeneralUser-GS/raw/main/GeneralUser-GS.sf2";

    private const string SaikaraDirectoryName = "Saikara";
    private const string SoundFontsDirectoryName = "soundfonts";

    private readonly HttpClient _httpClient;
    private readonly string _downloadUrl;

    /// <summary>
    /// Creates an installer.
    /// </summary>
    /// <param name="httpClient">
    /// The HTTP client used for the first-run download. Injected so it can be faked in tests;
    /// the caller owns its lifetime.
    /// </param>
    /// <param name="baseDirectory">
    /// The base directory the soundfonts folder lives under. Defaults to
    /// <see cref="Environment.SpecialFolder.LocalApplicationData"/>; tests point it at a temp
    /// directory.
    /// </param>
    /// <param name="downloadUrl">
    /// The SoundFont download URL. Defaults to <see cref="DefaultDownloadUrl"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/> is <see langword="null"/>.</exception>
    public SoundFontInstaller(
        HttpClient httpClient,
        string? baseDirectory = null,
        string? downloadUrl = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _downloadUrl = downloadUrl ?? DefaultDownloadUrl;

        string root = baseDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DefaultSoundFontPath = Path.Combine(
            root, SaikaraDirectoryName, SoundFontsDirectoryName, DefaultSoundFontFileName);
    }

    /// <summary>
    /// The resolved absolute path of the default SoundFont:
    /// <c>&lt;baseDirectory&gt;/Saikara/soundfonts/&lt;name&gt;.sf2</c>. The file may not exist
    /// yet; call <see cref="EnsureDefaultSoundFontAsync"/> to create it.
    /// </summary>
    public string DefaultSoundFontPath { get; }

    /// <summary>
    /// Ensures the default SoundFont exists at <see cref="DefaultSoundFontPath"/>, downloading
    /// it from the configured URL on first run. Idempotent: if the file is already present this
    /// returns immediately without contacting the network.
    /// </summary>
    /// <param name="progress">
    /// Optional progress reporter receiving a fraction in <c>[0, 1]</c>. When the server does
    /// not report a content length, progress jumps to <c>1.0</c> on completion.
    /// </param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The final <see cref="DefaultSoundFontPath"/>.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the download fails (network error or non-success status). No partial or temp
    /// file is left behind.
    /// </exception>
    public async Task<string> EnsureDefaultSoundFontAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string finalPath = DefaultSoundFontPath;
        if (File.Exists(finalPath))
        {
            progress?.Report(1.0);
            return finalPath;
        }

        string directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);

        // Download into a uniquely-named temp file in the SAME directory so the final move is
        // an atomic rename on the same volume; never leave a partial .sf2 in its place.
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.download");

        try
        {
            await DownloadToFileAsync(tempPath, progress, cancellationToken).ConfigureAwait(false);

            // Atomically move into place. If a concurrent caller won the race, keep theirs.
            if (File.Exists(finalPath))
            {
                File.Delete(tempPath);
            }
            else
            {
                File.Move(tempPath, finalPath);
            }
        }
        catch
        {
            TryDeleteTemp(tempPath);
            throw;
        }

        progress?.Report(1.0);
        return finalPath;
    }

    private async Task DownloadToFileAsync(
        string tempPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient
            .GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        await using Stream source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;

            if (progress is not null && total is > 0)
            {
                progress.Report(Math.Clamp((double)copied / total.Value, 0.0, 1.0));
            }
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best-effort cleanup; never mask the original failure.
        }
    }
}
