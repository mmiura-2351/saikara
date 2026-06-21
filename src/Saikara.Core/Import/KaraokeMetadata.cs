namespace Saikara.Core.Import;

/// <summary>
/// Title/artist metadata extracted from a Standard MIDI File / KAR file. Any field may be
/// <see langword="null"/> when the source carries no such information; the import service
/// then falls back to the file name.
/// </summary>
/// <param name="Title">The song title, or <see langword="null"/> when none was found.</param>
/// <param name="Artist">The performing artist, or <see langword="null"/> when none was found.</param>
public readonly record struct KaraokeMetadata(string? Title, string? Artist);
