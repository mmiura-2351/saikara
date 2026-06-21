using System.Security.Cryptography;
using Saikara.Core.Library;
using Saikara.Core.Midi;

namespace Saikara.Core.Import;

/// <summary>
/// Default <see cref="IMidiImportService"/>: validates a MIDI/KAR source by parsing it, copies the
/// bytes atomically into the library directory, derives metadata, and upserts a
/// <see cref="Song"/>. Platform-agnostic — the network access is via an injected
/// <see cref="HttpClient"/>, the parse via an injected <see cref="IMidiLoader"/>, and persistence
/// via an injected <see cref="ISongLibrary"/>, so the whole flow is unit-testable on Linux with no
/// real network.
/// </summary>
public sealed class MidiImportService : IMidiImportService
{
    private const string SaikaraDirectoryName = "Saikara";
    private const string LibraryDirectoryName = "library";

    /// <summary>Default file extension used when a source has no usable MIDI/KAR extension.</summary>
    private const string DefaultExtension = ".mid";

    private static readonly string[] AllowedExtensions = { ".mid", ".midi", ".kar" };

    private readonly ISongLibrary _library;
    private readonly IMidiLoader _loader;
    private readonly HttpClient _httpClient;
    private readonly string _libraryDirectory;

    /// <summary>
    /// Creates an import service.
    /// </summary>
    /// <param name="library">The library that imported songs are upserted into.</param>
    /// <param name="loader">The MIDI loader used to validate and read source bytes.</param>
    /// <param name="httpClient">
    /// The HTTP client used by <see cref="ImportUrlAsync"/>. Injected so URL imports are testable
    /// without real network access; the caller owns its lifetime.
    /// </param>
    /// <param name="libraryDirectory">
    /// The directory copied files are stored in. Defaults to
    /// <c>&lt;LocalApplicationData&gt;/Saikara/library</c> when <see langword="null"/>; tests point
    /// it at a temp directory.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="library"/>, <paramref name="loader"/> or
    /// <paramref name="httpClient"/> is <see langword="null"/>.
    /// </exception>
    public MidiImportService(
        ISongLibrary library,
        IMidiLoader loader,
        HttpClient httpClient,
        string? libraryDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(httpClient);

        _library = library;
        _loader = loader;
        _httpClient = httpClient;
        _libraryDirectory = libraryDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SaikaraDirectoryName,
                LibraryDirectoryName);
    }

    /// <summary>The directory imported files are copied into.</summary>
    public string LibraryDirectory => _libraryDirectory;

    /// <inheritdoc />
    public async Task<ImportResult> ImportFileAsync(
        string sourcePath,
        DateTimeOffset addedAt = default,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ImportException("A source file path is required.");
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ImportException($"Could not read the file '{sourcePath}'.", ex);
        }

        string preferredName = Path.GetFileName(sourcePath);
        string extension = PickExtension(sourcePath);

        return await ImportBytesAsync(bytes, preferredName, extension, addedAt, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportUrlAsync(
        string url,
        string? suggestedName = null,
        DateTimeOffset addedAt = default,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ImportException("A source URL is required.");
        }

        byte[] bytes = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);

        // Prefer the caller's suggested name, else the URL's last path segment, for both the
        // stored file name and the metadata fallback.
        string urlSegment = LastUrlSegment(url);
        string preferredName = !string.IsNullOrWhiteSpace(suggestedName)
            ? suggestedName!
            : urlSegment;
        string extension = PickExtension(urlSegment);

        return await ImportBytesAsync(bytes, preferredName, extension, addedAt, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            throw new ImportException($"Could not download '{url}'.", ex);
        }
    }

    /// <summary>
    /// The core flow shared by file and URL imports: validate, copy atomically, derive metadata,
    /// upsert. Guarantees that a validation failure adds no song and leaves no file behind.
    /// </summary>
    private async Task<ImportResult> ImportBytesAsync(
        byte[] bytes,
        string preferredName,
        string extension,
        DateTimeOffset addedAt,
        CancellationToken cancellationToken)
    {
        // 1) VALIDATE by parsing — reject anything that is not a Standard MIDI File before we
        //    write anything to disk.
        MidiSong song = Parse(bytes);

        // 2) COPY atomically into the library directory under a sanitized, unique name.
        Directory.CreateDirectory(_libraryDirectory);
        string destinationPath = await WriteToLibraryAsync(bytes, preferredName, extension, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // 3) DERIVE metadata.
            KaraokeMetadata metadata = KaraokeMetadataExtractor.Extract(song);
            string fallbackName = StripExtension(preferredName);
            string title = !string.IsNullOrWhiteSpace(metadata.Title) ? metadata.Title! : fallbackName;
            string artist = !string.IsNullOrWhiteSpace(metadata.Artist) ? metadata.Artist! : "Unknown";
            string number = GenerateNumber(bytes);
            int? melodyTrackIndex = MelodyTrackDetector.Detect(song);
            bool hasLyrics = song.Lyrics.Count > 0;

            // 4) UPSERT — keyed on Number, so re-importing identical bytes updates in place.
            Song stored = await _library.UpsertAsync(
                new Song
                {
                    Number = number,
                    Title = title,
                    Artist = artist,
                    FilePath = destinationPath,
                    MelodyTrackIndex = melodyTrackIndex,
                    DateAdded = addedAt,
                },
                cancellationToken).ConfigureAwait(false);

            return new ImportResult
            {
                Song = stored,
                HasLyrics = hasLyrics,
                MelodyTrackIndex = melodyTrackIndex,
                Duration = song.Duration,
            };
        }
        catch
        {
            // If persistence fails after the copy, do not leave an orphaned file behind.
            TryDelete(destinationPath);
            throw;
        }
    }

    private MidiSong Parse(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return _loader.Load(stream);
        }
        catch (Exception ex)
        {
            throw new ImportException(
                "The source is not a valid Standard MIDI File (.mid/.kar). " +
                "Only MIDI/KAR files can be imported.",
                ex);
        }
    }

    /// <summary>
    /// Atomically writes <paramref name="bytes"/> into the library directory: streams into a
    /// uniquely-named temp file in the same directory, then renames it into place. A name collision
    /// is resolved by appending a counter, so two different imports of the same name never clobber
    /// each other.
    /// </summary>
    private async Task<string> WriteToLibraryAsync(
        byte[] bytes,
        string preferredName,
        string extension,
        CancellationToken cancellationToken)
    {
        string baseName = Sanitize(StripExtension(preferredName));
        if (baseName.Length == 0)
        {
            baseName = "song";
        }

        string destinationPath = UniquePath(baseName, extension);
        string tempPath = Path.Combine(
            _libraryDirectory, $".{Guid.NewGuid():N}.import");

        try
        {
            await using (var destination = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, destinationPath);
            return destinationPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>Finds a free <c>&lt;dir&gt;/&lt;base&gt;[ (n)]&lt;ext&gt;</c> path.</summary>
    private string UniquePath(string baseName, string extension)
    {
        string candidate = Path.Combine(_libraryDirectory, baseName + extension);
        int counter = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(_libraryDirectory, $"{baseName} ({counter}){extension}");
            counter++;
        }

        return candidate;
    }

    /// <summary>
    /// Generates the karaoke <see cref="Song.Number"/> for an imported file when none is supplied.
    /// </summary>
    /// <remarks>
    /// The number is <c>IMP-</c> followed by the first 12 hex characters of the SHA-256 hash of the
    /// file bytes. This is deterministic and content-addressed: re-importing the exact same file
    /// produces the same number, so the upsert updates the existing row instead of creating a
    /// duplicate. Two genuinely different files get different numbers (collisions are astronomically
    /// unlikely at this length).
    /// </remarks>
    private static string GenerateNumber(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return "IMP-" + Convert.ToHexString(hash, 0, 6);
    }

    /// <summary>Picks a MIDI/KAR extension from a name, defaulting to <c>.mid</c>.</summary>
    private static string PickExtension(string name)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        return Array.Exists(AllowedExtensions, e => e == extension) ? extension : DefaultExtension;
    }

    private static string StripExtension(string name)
        => Path.GetFileNameWithoutExtension(name);

    /// <summary>Returns the last path segment of a URL (without query/fragment).</summary>
    private static string LastUrlSegment(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            string path = uri.AbsolutePath;
            string segment = path.TrimEnd('/');
            int slash = segment.LastIndexOf('/');
            if (slash >= 0 && slash < segment.Length - 1)
            {
                return Uri.UnescapeDataString(segment[(slash + 1)..]);
            }
        }

        // Fall back to whatever follows the last slash, ignoring any query string.
        string trimmed = url.Split('?', '#')[0].TrimEnd('/');
        int index = trimmed.LastIndexOf('/');
        return index >= 0 && index < trimmed.Length - 1 ? trimmed[(index + 1)..] : trimmed;
    }

    /// <summary>
    /// Replaces characters that are invalid in a file name (on any OS) with <c>_</c> and trims
    /// trailing dots/spaces so the result is a safe, portable file name.
    /// </summary>
    private static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        char[] chars = name.ToCharArray();
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars).Trim().TrimEnd('.', ' ').Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; never mask the original failure.
        }
    }
}
