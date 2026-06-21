using Saikara.Core.Music;
using Saikara.Core.Scoring;

namespace Saikara.Core.Tests.Scoring;

/// <summary>
/// Synthesizes <see cref="PitchSample"/> sequences and <see cref="ReferenceNote"/> lists for the
/// scoring tests, so no audio fixtures are needed. All generators are deterministic.
/// </summary>
internal static class ScoringTestData
{
    /// <summary>The analysis hop period used by the synthesized samples (≈ 11.6 ms, matching a ~512-sample hop at 44.1 kHz).</summary>
    public const double HopSeconds = 0.01161;

    public static ReferenceNote Note(double startSec, double durationSec, int midiNote)
        => ReferenceNote.Create(
            TimeSpan.FromSeconds(startSec),
            TimeSpan.FromSeconds(durationSec),
            midiNote);

    /// <summary>
    /// Fills the window <c>[start, start+duration)</c> with voiced samples at the given constant
    /// frequency (Hz), one per hop, with the given energy.
    /// </summary>
    public static IEnumerable<PitchSample> ConstantTone(
        double startSec, double durationSec, double frequencyHz, double energy = 0.0, double hopSeconds = HopSeconds)
    {
        int hops = Math.Max(1, (int)(durationSec / hopSeconds));
        for (int i = 0; i < hops; i++)
        {
            double t = startSec + i * hopSeconds;
            yield return PitchSample.VoicedAt(TimeSpan.FromSeconds(t), frequencyHz, clarity: 0.99, energy: energy);
        }
    }

    /// <summary>Same as <see cref="ConstantTone"/> but at a given MIDI note number (converted to Hz).</summary>
    public static IEnumerable<PitchSample> ConstantNote(
        double startSec, double durationSec, double midiNote, double energy = 0.0, double hopSeconds = HopSeconds)
        => ConstantTone(startSec, durationSec, MusicMath.MidiNoteToFrequency(midiNote), energy, hopSeconds);

    /// <summary>
    /// Voiced samples sung at <paramref name="midiNote"/> plus a sinusoidal pitch modulation of the
    /// given rate (Hz) and depth (cents, peak-to-peak), for use in vibrato tests.
    /// </summary>
    public static IEnumerable<PitchSample> VibratoNote(
        double startSec, double durationSec, double midiNote, double rateHz, double depthCents,
        double hopSeconds = HopSeconds)
    {
        int hops = Math.Max(1, (int)(durationSec / hopSeconds));
        double amplitudeCents = depthCents / 2.0;
        for (int i = 0; i < hops; i++)
        {
            double t = i * hopSeconds;
            double modCents = amplitudeCents * Math.Sin(2.0 * Math.PI * rateHz * t);
            double note = midiNote + modCents / 100.0;
            double hz = MusicMath.MidiNoteToFrequency(note);
            yield return PitchSample.VoicedAt(TimeSpan.FromSeconds(startSec + t), hz, clarity: 0.99);
        }
    }

    /// <summary>
    /// A shakuri onset: the first <paramref name="onsetSec"/> seconds rise linearly from
    /// <paramref name="startBelowCents"/> below the target up to it, then hold on the target.
    /// </summary>
    public static IEnumerable<PitchSample> ShakuriNote(
        double startSec, double durationSec, double midiNote, double onsetSec, double startBelowCents,
        double hopSeconds = HopSeconds)
    {
        int hops = Math.Max(1, (int)(durationSec / hopSeconds));
        for (int i = 0; i < hops; i++)
        {
            double t = i * hopSeconds;
            double offsetCents;
            if (t < onsetSec)
            {
                double frac = t / onsetSec; // 0 -> 1 over the onset window
                offsetCents = -startBelowCents * (1.0 - frac);
            }
            else
            {
                offsetCents = 0.0;
            }

            double note = midiNote + offsetCents / 100.0;
            double hz = MusicMath.MidiNoteToFrequency(note);
            yield return PitchSample.VoicedAt(TimeSpan.FromSeconds(startSec + t), hz, clarity: 0.99);
        }
    }

    /// <summary>
    /// A kobushi ornament: a sustained note on <paramref name="midiNote"/> with a brief upward bend
    /// of <paramref name="bendCents"/> centred at <paramref name="bendAtSec"/> that returns to pitch.
    /// </summary>
    public static IEnumerable<PitchSample> KobushiNote(
        double startSec, double durationSec, double midiNote, double bendAtSec, double bendCents,
        double bendWidthSec = 0.08, double hopSeconds = HopSeconds)
    {
        int hops = Math.Max(1, (int)(durationSec / hopSeconds));
        for (int i = 0; i < hops; i++)
        {
            double t = i * hopSeconds;
            double dist = Math.Abs(t - bendAtSec);
            // A narrow triangular bump up and back.
            double offsetCents = dist < bendWidthSec ? bendCents * (1.0 - dist / bendWidthSec) : 0.0;
            double note = midiNote + offsetCents / 100.0;
            double hz = MusicMath.MidiNoteToFrequency(note);
            yield return PitchSample.VoicedAt(TimeSpan.FromSeconds(startSec + t), hz, clarity: 0.99);
        }
    }
}
