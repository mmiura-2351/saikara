namespace Saikara.Core.Import;

/// <summary>
/// Brings songs into the library from a local MIDI/KAR file or a URL. This is the only sanctioned
/// content-acquisition path: it imports Standard MIDI Files (incl. <c>.kar</c> / Yamaha XF) only —
/// there is no streaming-audio ripping (requirements §6/§7).
/// </summary>
/// <remarks>
/// Implementations are platform-agnostic. Every import (1) validates the bytes by parsing them as
/// an SMF — a non-SMF source is rejected with an <see cref="ImportException"/> and leaves no
/// <see cref="Library.Song"/> and no partial file; (2) copies the bytes atomically into the
/// configured library directory with a sanitized, unique file name; (3) derives the song metadata;
/// and (4) upserts a <see cref="Library.Song"/> whose <c>FilePath</c> points at the copied file.
/// </remarks>
public interface IMidiImportService
{
    /// <summary>
    /// Imports a local <c>.mid</c> / <c>.kar</c> file into the library.
    /// </summary>
    /// <param name="sourcePath">Path to the MIDI/KAR file to import.</param>
    /// <param name="addedAt">
    /// The timestamp recorded as the song's <see cref="Library.Song.DateAdded"/> on first insert.
    /// The caller supplies it so this layer stays free of <c>DateTimeOffset.Now</c>. Defaults to
    /// <see cref="DateTimeOffset.UnixEpoch"/> when omitted; pass the real time from the UI.
    /// </param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The import result, including the stored song.</returns>
    /// <exception cref="ImportException">
    /// Thrown when the source cannot be read or is not a valid SMF/KAR file. No song is added and
    /// no partial file is left behind.
    /// </exception>
    Task<ImportResult> ImportFileAsync(
        string sourcePath,
        DateTimeOffset addedAt = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a MIDI/KAR file from <paramref name="url"/> and imports it into the library.
    /// </summary>
    /// <param name="url">The URL of the MIDI/KAR file to download.</param>
    /// <param name="suggestedName">
    /// An optional human-friendly name (e.g. a song title) used for the stored file name and as a
    /// metadata fallback. When omitted, the name is derived from the URL's last path segment.
    /// </param>
    /// <param name="addedAt">
    /// The timestamp recorded as the song's <see cref="Library.Song.DateAdded"/> on first insert.
    /// The caller supplies it; defaults to <see cref="DateTimeOffset.UnixEpoch"/> when omitted.
    /// </param>
    /// <param name="cancellationToken">Cancels the download and import.</param>
    /// <returns>The import result, including the stored song.</returns>
    /// <exception cref="ImportException">
    /// Thrown when the download fails or the downloaded bytes are not a valid SMF/KAR file. No song
    /// is added and no partial file is left behind.
    /// </exception>
    Task<ImportResult> ImportUrlAsync(
        string url,
        string? suggestedName = null,
        DateTimeOffset addedAt = default,
        CancellationToken cancellationToken = default);
}
