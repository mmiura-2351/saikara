using Saikara.Core.Midi;

namespace Saikara.Core.Lyrics;

/// <summary>
/// Turns the raw lyric/text meta-event stream of a <see cref="MidiSong"/> into the structured
/// two-line color-wipe telop model (<see cref="TelopLine"/> / <see cref="TelopSyllable"/>).
/// Implementations are pure: same input, same output, no side effects — so the result is
/// unit-testable and can be cached and reused with the audio engine's clock.
/// </summary>
public interface ILyricTelopBuilder
{
    /// <summary>Builds the telop lines from a song's <see cref="MidiSong.Lyrics"/>.</summary>
    /// <param name="song">The parsed song. Not mutated.</param>
    /// <returns>The telop lines in time order, or an empty list when the song carries no usable lyrics.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="song"/> is <see langword="null"/>.</exception>
    IReadOnlyList<TelopLine> Build(MidiSong song);

    /// <summary>Builds the telop lines directly from a lyric/text event stream.</summary>
    /// <param name="lyrics">Lyric/text events ordered by time. Not mutated.</param>
    /// <returns>The telop lines in time order, or an empty list when there are no usable lyrics.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lyrics"/> is <see langword="null"/>.</exception>
    IReadOnlyList<TelopLine> Build(IReadOnlyList<LyricEvent> lyrics);
}
