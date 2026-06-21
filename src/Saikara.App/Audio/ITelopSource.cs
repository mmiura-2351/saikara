using System;
using System.Collections.Generic;
using Saikara.Core.Lyrics;

namespace Saikara.App.Audio;

/// <summary>
/// App-level surface (implemented by <see cref="MeltySynthAudioEngine"/>) that exposes the lyric
/// telop alongside the playback clock, so the display view-model can drive a position-synced
/// color-wipe telop without depending on the concrete engine type.
/// </summary>
/// <remarks>
/// <para>
/// Saikara's platform-agnostic <c>Saikara.Core.IAudioEngine</c> deliberately knows nothing about
/// lyrics. The telop is built in the App from the <em>tempo-transformed</em> song the engine is
/// actually playing (so syllable start-times match the audio clock), which is engine-private state.
/// Rather than widen the Core contract, the App engine additionally implements this interface and
/// the display view-model consumes it. <c>Position</c> / <c>PositionChanged</c> are re-surfaced
/// here so a consumer that only holds an <see cref="ITelopSource"/> has everything it needs.
/// </para>
/// <para>
/// Threading: <see cref="TelopChanged"/> and <see cref="PositionChanged"/> are raised on the UI
/// thread by <see cref="MeltySynthAudioEngine"/> (it marshals through its UI
/// <see cref="Microsoft.UI.Dispatching.DispatcherQueue"/>). Consumers should still not assume it and
/// marshal UI work onto their own dispatcher when in doubt.
/// </para>
/// </remarks>
public interface ITelopSource
{
    /// <summary>
    /// The telop lines for the currently loaded song, in time order, built from the
    /// tempo-transformed song so each syllable's <see cref="TelopSyllable.StartTime"/> lines up with
    /// <see cref="Position"/>. Empty when no song is loaded or the song carries no usable lyrics.
    /// Never <see langword="null"/>.
    /// </summary>
    IReadOnlyList<TelopLine> CurrentTelopLines { get; }

    /// <summary>The current playback position from the start of the song (mirrors the engine clock).</summary>
    TimeSpan Position { get; }

    /// <summary>
    /// Raised when <see cref="CurrentTelopLines"/> changes — on load and on a tempo change (tempo
    /// rescales lyric times, so the lines are rebuilt to keep the wipe in sync). Raised on the UI
    /// thread.
    /// </summary>
    event EventHandler? TelopChanged;

    /// <summary>Raised as the playback position advances (mirrors the engine's position ticks). Raised on the UI thread.</summary>
    event EventHandler? PositionChanged;
}
