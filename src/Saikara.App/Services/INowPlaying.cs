using Saikara.Core.Library;

namespace Saikara.App.Services;

/// <summary>
/// Singleton holder of the song currently loaded into the audio engine, shared between the
/// operator window (which sets it on load) and the display window (which reads it when a score
/// is produced at song end, to attach the score to the right library entry). It exists so the
/// two view-models, built independently by DI, agree on "what is playing" without one referencing
/// the other.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CurrentSong"/> is the library <see cref="Song"/> that backs the loaded MIDI, or
/// <see langword="null"/> for an ad-hoc "Open MIDI file…" load that is not a library entry. The
/// karaoke <see cref="Song.Number"/> of a non-null value keys the per-song score history; a null
/// value means the performance has no library number (history is still recorded against an empty
/// number, but not surfaced as a per-song best/recent list).
/// </para>
/// <para>
/// This carries no threading guarantees of its own: writes happen from the operator UI thread on
/// load and reads happen from the display UI thread at song end, both marshaled by their callers.
/// </para>
/// </remarks>
public interface INowPlaying
{
    /// <summary>
    /// The library <see cref="Song"/> currently loaded into the engine, or <see langword="null"/>
    /// for an ad-hoc opened file. Set by the operator on every load; read by the display when a
    /// score is produced.
    /// </summary>
    Song? CurrentSong { get; set; }
}
