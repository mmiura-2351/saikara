using Saikara.Core.Midi;

namespace Saikara.Core.Audio;

/// <summary>
/// Transport state of an <see cref="IAudioEngine"/>.
/// </summary>
public enum PlaybackState
{
    /// <summary>No song is playing; position is at zero (or wherever a seek left it).</summary>
    Stopped,

    /// <summary>A song is currently being rendered to the audio device.</summary>
    Playing,

    /// <summary>Playback is suspended at the current <see cref="IAudioEngine.Position"/>.</summary>
    Paused,
}

/// <summary>
/// The platform-agnostic contract for MIDI/SoundFont playback that the operator and display
/// UI bind to. The concrete implementation (<c>MeltySynthAudioEngine</c>) lives in
/// <c>Saikara.App</c> and combines MeltySynth (SoundFont rendering) with NAudio (WASAPI
/// output); it must not leak any Windows or audio-device type into this interface.
/// </summary>
/// <remarks>
/// Key and tempo control are expressed as a semitone offset and a tempo percentage. The
/// implementation applies them by re-deriving the playing sequence from the loaded
/// <see cref="MidiSong"/> via <see cref="MidiTransforms"/> (transpose / scale tempo) and
/// re-feeding the transformed song to the synth — so the abstraction stays free of any
/// real-time DSP. Setting either while playing should take effect as soon as practical.
/// </remarks>
public interface IAudioEngine : IDisposable
{
    /// <summary>
    /// Sets the current song. Resets <see cref="Position"/> to zero, updates
    /// <see cref="Duration"/>, and leaves the engine <see cref="PlaybackState.Stopped"/>.
    /// The supplied song is the untransformed source; <see cref="SemitoneOffset"/> and
    /// <see cref="TempoPercent"/> are reapplied on top of it.
    /// </summary>
    /// <param name="song">The song to play. Not mutated.</param>
    void Load(MidiSong song);

    /// <summary>
    /// Starts (or resumes) playback from the current <see cref="Position"/>. No-op if already
    /// <see cref="PlaybackState.Playing"/>; throws if no song has been loaded.
    /// </summary>
    void Play();

    /// <summary>
    /// Suspends playback, keeping the current <see cref="Position"/>. No-op unless
    /// <see cref="PlaybackState.Playing"/>.
    /// </summary>
    void Pause();

    /// <summary>
    /// Stops playback and rewinds <see cref="Position"/> to zero. No-op if already
    /// <see cref="PlaybackState.Stopped"/>.
    /// </summary>
    void Stop();

    /// <summary>
    /// Moves the playback position to <paramref name="position"/>, clamped to
    /// <c>[TimeSpan.Zero, <see cref="Duration"/>]</c>. May be called in any state and does
    /// not by itself change <see cref="State"/>.
    /// </summary>
    /// <param name="position">The target position from the start of the song.</param>
    void Seek(TimeSpan position);

    /// <summary>The current transport state.</summary>
    PlaybackState State { get; }

    /// <summary>The current playback position from the start of the song.</summary>
    TimeSpan Position { get; }

    /// <summary>
    /// The total duration of the currently loaded song, reflecting the active
    /// <see cref="TempoPercent"/> (a slower tempo lengthens it). <see cref="TimeSpan.Zero"/>
    /// when no song is loaded.
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Key change in semitones applied to the loaded song (0 == original key). Positive
    /// transposes up, negative down. Setting this re-derives the playing sequence via
    /// <see cref="MidiTransforms.Transpose(MidiSong, int)"/>; the percussion channel is left
    /// unchanged. The change applies while playing without losing the current position.
    /// </summary>
    int SemitoneOffset { get; set; }

    /// <summary>
    /// Tempo as a percentage of the song's original tempo (100 == original; 150 == 1.5x
    /// faster; 50 == half speed). Must be positive. Setting this re-derives the playing
    /// sequence via <see cref="MidiTransforms.ScaleTempo(MidiSong, double)"/> and updates
    /// <see cref="Duration"/>; pitch is unaffected.
    /// </summary>
    double TempoPercent { get; set; }

    /// <summary>
    /// Raised when <see cref="State"/> changes (e.g. play -> pause, or reaching the end).
    /// May be raised on a background thread; UI handlers must marshal to their dispatcher.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Raised periodically while playing as <see cref="Position"/> advances, so the telop and
    /// transport UI can follow along. May be raised on a background thread.
    /// </summary>
    event EventHandler? PositionChanged;
}
