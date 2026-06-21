using Saikara.Core.Midi;

namespace Saikara.Core.Editor;

/// <summary>
/// The result of applying <see cref="SongCorrections"/> to a <see cref="MidiSong"/> via
/// <see cref="CorrectedSongBuilder.Apply"/>. Carries the original song, the adjusted lyric
/// events (with timing shifts applied), and the overridden melody track index. The engine
/// uses this instead of reading lyrics/melody directly from <see cref="MidiSong"/>.
/// </summary>
public sealed record CorrectedSong
{
    /// <summary>The original, unmodified <see cref="MidiSong"/>.</summary>
    public required MidiSong Song { get; init; }

    /// <summary>
    /// Lyric events with <see cref="SongCorrections.LyricOffsetMs"/> and per-syllable
    /// adjustments applied. When no corrections were given, this is the same list as
    /// <see cref="MidiSong.Lyrics"/>. Times are clamped to &gt;= 0.
    /// </summary>
    public required IReadOnlyList<LyricEvent> Lyrics { get; init; }

    /// <summary>
    /// The user's melody track index override, or <see langword="null"/> when no override
    /// was specified (callers should fall back to <see cref="MelodyTrackDetector.Detect"/>
    /// or <see cref="Library.Song.MelodyTrackIndex"/>).
    /// </summary>
    public int? MelodyTrackIndex { get; init; }
}
