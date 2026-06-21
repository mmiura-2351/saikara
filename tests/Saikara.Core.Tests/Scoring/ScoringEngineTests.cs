using Saikara.Core.Music;
using Saikara.Core.Scoring;
using static Saikara.Core.Tests.Scoring.ScoringTestData;

namespace Saikara.Core.Tests.Scoring;

public class ScoringEngineTests
{
    private static readonly ScoringEngine Engine = new();

    // ---------------------------------------------------------------------------------------
    // Empty / defined-result behaviour
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Score_EmptySung_ReturnsEmptyResult_NoThrow()
    {
        var reference = new[] { Note(0, 1, 60) };
        ScoreResult r = Engine.Score(Array.Empty<PitchSample>(), reference);

        Assert.Equal(ScoreResult.Empty, r);
        Assert.Equal(0.0, r.Overall);
    }

    [Fact]
    public void Score_EmptyReference_ReturnsEmptyResult_NoThrow()
    {
        IReadOnlyList<PitchSample> sung = ConstantNote(0, 1, 60).ToList();
        ScoreResult r = Engine.Score(sung, Array.Empty<ReferenceNote>());

        Assert.Equal(ScoreResult.Empty, r);
    }

    [Fact]
    public void Score_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Engine.Score(null!, Array.Empty<ReferenceNote>()));
        Assert.Throws<ArgumentNullException>(() => Engine.Score(Array.Empty<PitchSample>(), null!));
    }

    // ---------------------------------------------------------------------------------------
    // Pitch accuracy (音程)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void PitchAccuracy_PerfectOnPitch_IsNearHundred()
    {
        var reference = new[] { Note(0, 1, 60), Note(1, 1, 62), Note(2, 1, 64) };
        var sung = new List<PitchSample>();
        sung.AddRange(ConstantNote(0, 1, 60));
        sung.AddRange(ConstantNote(1, 1, 62));
        sung.AddRange(ConstantNote(2, 1, 64));

        ScoreResult r = Engine.Score(sung, reference);

        Assert.True(r.PitchAccuracy >= 99.0, $"expected ~100, got {r.PitchAccuracy}");
    }

    [Fact]
    public void PitchAccuracy_ConsistentlyOffByMoreThanTolerance_IsLow()
    {
        var reference = new[] { Note(0, 1, 60), Note(1, 1, 62) };
        // Sing a full semitone (100 cents) sharp on every note — beyond the 50-cent tolerance.
        var sung = new List<PitchSample>();
        sung.AddRange(ConstantNote(0, 1, 61));
        sung.AddRange(ConstantNote(1, 1, 63));

        ScoreResult r = Engine.Score(sung, reference);

        Assert.True(r.PitchAccuracy <= 5.0, $"expected low, got {r.PitchAccuracy}");
    }

    [Fact]
    public void PitchAccuracy_WithinTolerance_StillCounts()
    {
        var reference = new[] { Note(0, 1, 60) };
        // 40 cents sharp — inside the 50-cent default tolerance.
        var sung = ConstantNote(0, 1, 60.4).ToList();

        ScoreResult r = Engine.Score(sung, reference);

        Assert.True(r.PitchAccuracy >= 99.0, $"expected ~100, got {r.PitchAccuracy}");
    }

    // ---------------------------------------------------------------------------------------
    // Octave-agnostic
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void OctaveAgnostic_On_OctaveOffStillScoresHigh()
    {
        var reference = new[] { Note(0, 1, 60) };
        // Sing one octave up.
        var sung = ConstantNote(0, 1, 72).ToList();

        ScoreResult r = Engine.Score(sung, reference, new ScoringOptions { OctaveAgnostic = true });

        Assert.True(r.PitchAccuracy >= 99.0, $"expected ~100, got {r.PitchAccuracy}");
    }

    [Fact]
    public void OctaveAgnostic_Off_OctaveOffScoresLow()
    {
        var reference = new[] { Note(0, 1, 60) };
        var sung = ConstantNote(0, 1, 72).ToList();

        ScoreResult r = Engine.Score(sung, reference, new ScoringOptions { OctaveAgnostic = false });

        Assert.True(r.PitchAccuracy <= 5.0, $"expected low, got {r.PitchAccuracy}");
    }

    // ---------------------------------------------------------------------------------------
    // Stability (安定性)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Stability_SteadyNote_HigherThanWobblyNote()
    {
        var reference = new[] { Note(0, 2, 60) };

        IReadOnlyList<PitchSample> steady = ConstantNote(0, 2, 60).ToList();

        // Wobbly: a large, slow wander around the target (not in the vibrato band).
        var wobbly = new List<PitchSample>();
        int hops = (int)(2.0 / HopSeconds);
        for (int i = 0; i < hops; i++)
        {
            double t = i * HopSeconds;
            double cents = 120.0 * Math.Sin(2.0 * Math.PI * 1.0 * t); // 1 Hz, ±120 cents
            double hz = MusicMath.MidiNoteToFrequency(60 + cents / 100.0);
            wobbly.Add(PitchSample.VoicedAt(TimeSpan.FromSeconds(t), hz));
        }

        ScoreResult steadyResult = Engine.Score(steady, reference);
        ScoreResult wobblyResult = Engine.Score(wobbly, reference);

        Assert.True(steadyResult.Stability > wobblyResult.Stability + 20.0,
            $"steady {steadyResult.Stability} vs wobbly {wobblyResult.Stability}");
        Assert.True(steadyResult.Stability >= 95.0, $"steady should be ~100, got {steadyResult.Stability}");
    }

    [Fact]
    public void Stability_NoSustainedNotes_IsNeutral()
    {
        // Only a very short note (< 4 hops of voicing) -> nothing sustained to judge -> neutral, not zero.
        var reference = new[] { Note(0, 0.02, 60) };
        var sung = ConstantNote(0, 0.02, 60).ToList();

        ScoreResult r = Engine.Score(sung, reference);

        Assert.InRange(r.Stability, 60.0, 80.0);
    }

    // ---------------------------------------------------------------------------------------
    // Long tone (ロングトーン)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void LongTone_HeldNoteOnPitch_IsRewarded_ShortNotesIgnored()
    {
        // One long note (2 s > 1 s threshold) plus short notes that must not count.
        var reference = new[]
        {
            Note(0, 2, 60),
            Note(2, 0.2, 62),
            Note(2.2, 0.2, 64),
        };

        var sung = new List<PitchSample>();
        sung.AddRange(ConstantNote(0, 2, 60));
        // Sing the short notes badly to prove they're ignored by long-tone.
        sung.AddRange(ConstantNote(2, 0.2, 50));
        sung.AddRange(ConstantNote(2.2, 0.2, 50));

        ScoreResult r = Engine.Score(sung, reference);

        Assert.True(r.LongTone >= 90.0, $"expected long held note rewarded, got {r.LongTone}");
    }

    [Fact]
    public void LongTone_NoLongNotes_IsNeutral()
    {
        var reference = new[] { Note(0, 0.3, 60), Note(0.3, 0.3, 62) };
        var sung = new List<PitchSample>();
        sung.AddRange(ConstantNote(0, 0.3, 60));
        sung.AddRange(ConstantNote(0.3, 0.3, 62));

        ScoreResult r = Engine.Score(sung, reference);

        Assert.InRange(r.LongTone, 60.0, 80.0);
    }

    [Fact]
    public void LongTone_HeldNoteSungOffPitch_IsLow()
    {
        var reference = new[] { Note(0, 2, 60) };
        var sung = ConstantNote(0, 2, 55).ToList(); // a perfect fourth flat, way off

        ScoreResult r = Engine.Score(sung, reference);

        Assert.True(r.LongTone <= 20.0, $"expected low, got {r.LongTone}");
    }

    // ---------------------------------------------------------------------------------------
    // Vibrato (ビブラート)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Vibrato_InBandModulation_IsCounted_AndBonusApplied()
    {
        var reference = new[] { Note(0, 2, 60) };
        // 6 Hz, 60 cents peak-to-peak — squarely in the 4-8 Hz band, above 30-cent depth.
        var sung = VibratoNote(0, 2, 60, rateHz: 6.0, depthCents: 60.0).ToList();

        ScoreResult withVibrato = Engine.Score(sung, reference);

        Assert.True(withVibrato.VibratoCount >= 1, "expected at least one vibrato");

        // The bonus must lift the overall above the same line sung flat.
        var flat = ConstantNote(0, 2, 60).ToList();
        ScoreResult withoutVibrato = Engine.Score(flat, reference);
        Assert.True(withVibrato.Overall > withoutVibrato.Overall,
            $"vibrato overall {withVibrato.Overall} should exceed flat {withoutVibrato.Overall}");
    }

    [Fact]
    public void Vibrato_OutOfBandModulation_IsNotCounted()
    {
        var reference = new[] { Note(0, 2, 60) };
        // 12 Hz is above the 8 Hz max band -> not vibrato.
        var fast = VibratoNote(0, 2, 60, rateHz: 12.0, depthCents: 60.0).ToList();
        // 1 Hz is below the 4 Hz min band -> not vibrato.
        var slow = VibratoNote(0, 2, 60, rateHz: 1.0, depthCents: 60.0).ToList();

        Assert.Equal(0, Engine.Score(fast, reference).VibratoCount);
        Assert.Equal(0, Engine.Score(slow, reference).VibratoCount);
    }

    [Fact]
    public void Vibrato_TooShallow_IsNotCounted()
    {
        var reference = new[] { Note(0, 2, 60) };
        // In-band rate but only 10 cents depth — below the 30-cent threshold.
        var shallow = VibratoNote(0, 2, 60, rateHz: 6.0, depthCents: 10.0).ToList();

        Assert.Equal(0, Engine.Score(shallow, reference).VibratoCount);
    }

    // ---------------------------------------------------------------------------------------
    // Shakuri (しゃくり)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Shakuri_OnsetRisingFromBelow_IsCounted()
    {
        var reference = new[] { Note(0, 1, 60) };
        // Start 150 cents below and slide up into the note over a 0.2 s onset.
        var sung = ShakuriNote(0, 1, 60, onsetSec: 0.2, startBelowCents: 150.0).ToList();

        ScoreResult r = Engine.Score(sung, reference);

        Assert.True(r.ShakuriCount >= 1, $"expected shakuri, got {r.ShakuriCount}");
    }

    [Fact]
    public void Shakuri_CleanOnset_IsNotCounted()
    {
        var reference = new[] { Note(0, 1, 60) };
        var sung = ConstantNote(0, 1, 60).ToList(); // starts on pitch, no slide

        Assert.Equal(0, Engine.Score(sung, reference).ShakuriCount);
    }

    // ---------------------------------------------------------------------------------------
    // Kobushi (こぶし)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Kobushi_MidNoteUpAndBackBend_IsCounted()
    {
        var reference = new[] { Note(0, 2, 60) };
        // A brief +120-cent bend at 1.0 s that returns to pitch.
        var sung = KobushiNote(0, 2, 60, bendAtSec: 1.0, bendCents: 120.0).ToList();

        ScoreResult r = Engine.Score(sung, reference);

        Assert.True(r.KobushiCount >= 1, $"expected kobushi, got {r.KobushiCount}");
    }

    [Fact]
    public void Kobushi_PlainHeldNote_IsNotCounted()
    {
        var reference = new[] { Note(0, 2, 60) };
        var sung = ConstantNote(0, 2, 60).ToList();

        Assert.Equal(0, Engine.Score(sung, reference).KobushiCount);
    }

    // ---------------------------------------------------------------------------------------
    // Expression (抑揚)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Expression_EnergyVariation_ScoresHigherThanFlatEnergy()
    {
        var reference = new[] { Note(0, 1, 60), Note(1, 1, 60) };

        // Varied energy: loud then soft.
        var varied = new List<PitchSample>();
        varied.AddRange(ConstantNote(0, 1, 60, energy: 0.9));
        varied.AddRange(ConstantNote(1, 1, 60, energy: 0.2));

        // Flat energy: constant loudness.
        var flat = new List<PitchSample>();
        flat.AddRange(ConstantNote(0, 1, 60, energy: 0.5));
        flat.AddRange(ConstantNote(1, 1, 60, energy: 0.5));

        ScoreResult variedResult = Engine.Score(varied, reference);
        ScoreResult flatResult = Engine.Score(flat, reference);

        Assert.True(variedResult.Expression > flatResult.Expression,
            $"varied {variedResult.Expression} should exceed flat {flatResult.Expression}");
    }

    [Fact]
    public void Expression_AllZeroEnergy_IsNeutralNotZero()
    {
        var reference = new[] { Note(0, 1, 60) };
        var sung = ConstantNote(0, 1, 60).ToList(); // energy defaults to 0 (unknown)

        ScoreResult r = Engine.Score(sung, reference);

        Assert.InRange(r.Expression, 60.0, 80.0);
        Assert.NotEqual(0.0, r.Expression);
    }

    // ---------------------------------------------------------------------------------------
    // Overall
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Overall_AlwaysWithinRange()
    {
        var reference = new[] { Note(0, 1, 60), Note(1, 1, 62) };

        // Terrible performance.
        var bad = new List<PitchSample>();
        bad.AddRange(ConstantNote(0, 1, 50));
        bad.AddRange(ConstantNote(1, 1, 51));

        // Great performance.
        var good = new List<PitchSample>();
        good.AddRange(ConstantNote(0, 1, 60, energy: 0.8));
        good.AddRange(ConstantNote(1, 1, 62, energy: 0.3));

        ScoreResult badResult = Engine.Score(bad, reference);
        ScoreResult goodResult = Engine.Score(good, reference);

        Assert.InRange(badResult.Overall, 0.0, 100.0);
        Assert.InRange(goodResult.Overall, 0.0, 100.0);
        Assert.True(goodResult.Overall > badResult.Overall,
            $"good {goodResult.Overall} should exceed bad {badResult.Overall}");
    }

    [Fact]
    public void Overall_IncreasesAsSingingImproves()
    {
        var reference = new[] { Note(0, 1, 60), Note(1, 1, 62), Note(2, 1, 64) };

        // Half on-pitch, half off.
        var partial = new List<PitchSample>();
        partial.AddRange(ConstantNote(0, 1, 60));
        partial.AddRange(ConstantNote(1, 1, 62));
        partial.AddRange(ConstantNote(2, 1, 55)); // off

        // All on-pitch.
        var full = new List<PitchSample>();
        full.AddRange(ConstantNote(0, 1, 60));
        full.AddRange(ConstantNote(1, 1, 62));
        full.AddRange(ConstantNote(2, 1, 64));

        Assert.True(Engine.Score(full, reference).Overall > Engine.Score(partial, reference).Overall);
    }

    [Fact]
    public void Overall_RespectsWeights_PitchWeightDominates()
    {
        var reference = new[] { Note(0, 2, 60) };
        // Sing perfectly on pitch but with no energy variation, so stability/longtone are high and
        // expression is neutral; with all weight on pitch the overall pins to the pitch score.
        var sung = ConstantNote(0, 2, 60).ToList();

        var pitchOnly = new ScoringOptions
        {
            PitchWeight = 100.0,
            StabilityWeight = 0.0,
            LongToneWeight = 0.0,
            ExpressionWeight = 0.0,
            VibratoBonusCap = 0.0,
            ShakuriBonusCap = 0.0,
            KobushiBonusCap = 0.0,
        };

        ScoreResult r = Engine.Score(sung, reference, pitchOnly);

        Assert.Equal(r.PitchAccuracy, r.Overall, precision: 6);
    }

    [Fact]
    public void Score_IsDeterministic()
    {
        var reference = new[] { Note(0, 2, 60), Note(2, 1, 62) };
        var sung = new List<PitchSample>();
        sung.AddRange(VibratoNote(0, 2, 60, rateHz: 6.0, depthCents: 60.0));
        sung.AddRange(ConstantNote(2, 1, 62, energy: 0.6));

        ScoreResult a = Engine.Score(sung, reference);
        ScoreResult b = Engine.Score(sung, reference);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Grade_MapsOverallToLetter()
    {
        Assert.Equal("S", (ScoreResult.Empty with { Overall = 95 }).Grade);
        Assert.Equal("A", (ScoreResult.Empty with { Overall = 85 }).Grade);
        Assert.Equal("B", (ScoreResult.Empty with { Overall = 75 }).Grade);
        Assert.Equal("C", (ScoreResult.Empty with { Overall = 65 }).Grade);
        Assert.Equal("D", (ScoreResult.Empty with { Overall = 55 }).Grade);
        Assert.Equal("E", (ScoreResult.Empty with { Overall = 10 }).Grade);
    }
}
