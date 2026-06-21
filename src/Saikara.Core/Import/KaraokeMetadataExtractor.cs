using Saikara.Core.Midi;

namespace Saikara.Core.Import;

/// <summary>
/// Extracts a song's title and artist from a parsed <see cref="MidiSong"/>.
/// </summary>
/// <remarks>
/// <para>
/// KAR files (the karaoke variant of an SMF) carry metadata as <c>Text</c> meta events,
/// conventionally placed near the start of the file. The de-facto "soft karaoke" convention
/// tags each line with a leading character:
/// </para>
/// <list type="bullet">
///   <item><description><c>@T</c> — title lines (the first <c>@T</c> line is the song title;
///     subsequent <c>@T</c> lines are extra info such as the album or composer).</description></item>
///   <item><description><c>@A</c> — author/artist line.</description></item>
///   <item><description><c>@I</c> — general information; many files use it for the artist when no
///     <c>@A</c> line is present.</description></item>
///   <item><description><c>@L</c> / <c>@W</c> — language / copyright (ignored here).</description></item>
///   <item><description><c>@K</c> — the <c>@KMIDI KARAOKE FILE</c> marker (ignored).</description></item>
/// </list>
/// <para>
/// The <c>@</c> tags appear in the lyric/text stream of the file. When no such tags are present
/// the extractor falls back to the MIDI sequence/track name (the first named track). Everything is
/// best-effort and tolerant of missing data: any field may come back <see langword="null"/>.
/// </para>
/// </remarks>
public static class KaraokeMetadataExtractor
{
    /// <summary>
    /// Derives <see cref="KaraokeMetadata"/> from a parsed song.
    /// </summary>
    /// <param name="song">The parsed MIDI/KAR song to inspect.</param>
    /// <returns>The extracted metadata; fields are <see langword="null"/> when absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="song"/> is <see langword="null"/>.</exception>
    public static KaraokeMetadata Extract(MidiSong song)
    {
        ArgumentNullException.ThrowIfNull(song);

        string? title = null;
        string? artist = null;
        string? info = null;

        foreach (LyricEvent lyric in song.Lyrics)
        {
            string text = lyric.Text;
            if (string.IsNullOrEmpty(text) || text[0] != '@')
            {
                continue;
            }

            if (text.Length < 2)
            {
                continue;
            }

            // Tag is the single character after '@'; the value is the rest of the line.
            char tag = char.ToUpperInvariant(text[1]);
            string value = text[2..].Trim();
            if (value.Length == 0)
            {
                continue;
            }

            switch (tag)
            {
                case 'T' when title is null:
                    title = value;
                    break;
                case 'A' when artist is null:
                    artist = value;
                    break;
                case 'I' when info is null:
                    info = value;
                    break;
            }
        }

        // Many files use @I for the artist when there is no explicit @A line.
        artist ??= info;

        // Fall back to the first named track for a title when no @T tag was present.
        title ??= FirstTrackName(song);

        return new KaraokeMetadata(title, artist);
    }

    private static string? FirstTrackName(MidiSong song)
    {
        foreach (MidiTrack track in song.Tracks)
        {
            if (!string.IsNullOrWhiteSpace(track.Name))
            {
                return track.Name!.Trim();
            }
        }

        return null;
    }
}
