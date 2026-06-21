using System;
using System.Collections.Generic;
using Saikara.Core.Scoring;

namespace Saikara.App.Audio;

/// <summary>
/// App-level surface (implemented by <see cref="MeltySynthAudioEngine"/>) that exposes the
/// detected reference melody — the <see cref="ReferenceNote"/> sequence the singer is scored and
/// visualised against — for the currently loaded song, already key- and tempo-adjusted to match
/// what the singer hears.
/// </summary>
/// <remarks>
/// <para>
/// Saikara's platform-agnostic <c>Saikara.Core.IAudioEngine</c> deliberately knows nothing about
/// scoring or melody references. The reference notes are derived in the App from the
/// <em>tempo- and key-transformed</em> song the engine is actually playing (so note start-times and
/// pitches line up with both the audio clock and the transposed backing), which is engine-private
/// state. Rather than widen the Core contract, the App engine additionally implements this interface
/// — mirroring how <see cref="ITelopSource"/> exposes lyrics — and the display / pitch-monitor
/// layer consumes it.
/// </para>
/// <para>
/// Because the reference is derived from the <em>already transformed</em> song, its MIDI notes
/// already include the current key offset; a consumer must NOT transpose them again. The offset
/// passed to <see cref="ReferenceMelody.FromTrack"/> is therefore <c>0</c>.
/// </para>
/// <para>
/// Threading: <see cref="ReferenceChanged"/> is raised on the UI thread by
/// <see cref="MeltySynthAudioEngine"/> (it marshals through its UI
/// <see cref="Microsoft.UI.Dispatching.DispatcherQueue"/>). Consumers should still marshal their own
/// UI work when in doubt.
/// </para>
/// </remarks>
public interface IReferenceSource
{
    /// <summary>
    /// The reference-melody notes for the currently loaded song, ordered by start time, with each
    /// note's <see cref="ReferenceNote.Start"/> aligned to the engine's playback clock and its
    /// <see cref="ReferenceNote.MidiNote"/> already reflecting the current key offset. Empty when no
    /// song is loaded, no melody track could be detected, or the melody track has no notes. Never
    /// <see langword="null"/>.
    /// </summary>
    IReadOnlyList<ReferenceNote> CurrentReferenceNotes { get; }

    /// <summary>
    /// Raised when <see cref="CurrentReferenceNotes"/> changes — on load and on any key or tempo
    /// change (each rebuilds the playing sequence, which re-derives the reference from the new
    /// transposed / rescaled song). Raised on the UI thread.
    /// </summary>
    event EventHandler? ReferenceChanged;
}
