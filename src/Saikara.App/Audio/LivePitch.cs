using Saikara.Core.Pitch;

namespace Saikara.App.Audio;

/// <summary>
/// The latest live microphone analysis hop surfaced by <see cref="IPitchMonitor"/> for the
/// real-time pitch bar: the detector outcome plus the loudness (RMS energy) of the hop. Immutable
/// and allocation-free so it can be raised at the hop rate (~43 Hz) without GC pressure.
/// </summary>
/// <remarks>
/// This is the <em>display</em> view of a hop. The scoring view of the same hop — a
/// latency-corrected <see cref="Saikara.Core.Scoring.PitchSample"/> — is accumulated separately by
/// the monitor (see <see cref="IPitchMonitor.CollectedSamples"/>). Keeping the two apart means the
/// pitch bar reacts to the raw, un-delayed detection while scoring uses the latency-aligned copy.
/// </remarks>
public readonly record struct LivePitch
{
    /// <summary>The detector result for the most recent analysis hop.</summary>
    public PitchResult Result { get; init; }

    /// <summary>
    /// Loudness of the hop in <c>[0, 1]</c>, a clamped RMS of the windowed samples. Drives marker
    /// intensity and the (P4) expression metric.
    /// </summary>
    public double Energy { get; init; }

    /// <summary>Whether the hop contained a pitched (sung) signal (mirrors <see cref="PitchResult.IsVoiced"/>).</summary>
    public bool IsVoiced => Result.IsVoiced;

    /// <summary>
    /// The detected (possibly fractional) MIDI note for a voiced hop, or <see langword="null"/> when
    /// unvoiced. The fractional part expresses how sharp/flat the pitch is and is what the pitch bar
    /// maps onto its vertical axis.
    /// </summary>
    public double? MidiNote => Result.ToMidiNote();

    /// <summary>A shared "no signal yet" value: an unvoiced result with zero energy.</summary>
    public static LivePitch Silent { get; } = new() { Result = PitchResult.Unvoiced, Energy = 0.0 };
}
