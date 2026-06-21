using Saikara.Core.Music;

namespace Saikara.Core.Scoring;

/// <summary>
/// The default DAM-style scoring engine. Pure and deterministic: it takes a finished
/// performance (a time-ordered list of <see cref="PitchSample"/>) and the reference melody
/// (<see cref="ReferenceNote"/>) and produces a <see cref="ScoreResult"/>. It does no audio I/O;
/// the mic-capture and pitch-detection layers feed it samples.
/// </summary>
/// <remarks>
/// <para>
/// Each metric is computed independently over the voiced samples that fall inside reference
/// notes, then blended by the weights in <see cref="ScoringOptions"/>. Pitch error is measured
/// in cents via <see cref="MusicMath"/> and octave-folded when
/// <see cref="ScoringOptions.OctaveAgnostic"/> is set.
/// </para>
/// <para>
/// "Neutral" sub-scores: stability, long-tone and expression return a mid-range value when there
/// is nothing to judge (no sustained notes, no long notes, or unknown energy) so a performance is
/// never penalised for the absence of data.
/// </para>
/// </remarks>
public sealed class ScoringEngine : IScoringEngine
{
    /// <summary>
    /// Sub-score returned when a metric has nothing to evaluate; chosen so the absence of data
    /// neither rewards nor punishes the overall score relative to a typical performance.
    /// </summary>
    private const double NeutralScore = 70.0;

    /// <inheritdoc />
    public ScoreResult Score(
        IReadOnlyList<PitchSample> sung,
        IReadOnlyList<ReferenceNote> reference,
        ScoringOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sung);
        ArgumentNullException.ThrowIfNull(reference);

        if (sung.Count == 0 || reference.Count == 0)
        {
            return ScoreResult.Empty;
        }

        ScoringOptions opt = options ?? ScoringOptions.Default;

        // Pre-sort reference notes; samples are assumed ordered but we don't rely on it.
        var notes = reference.OrderBy(n => n.Start).ToArray();
        var samples = sung.OrderBy(s => s.Time).ToArray();

        // Group the voiced samples that fall inside each reference note. A sample is matched to a
        // note when its time is within [Start, End); ornament detection works on these groups.
        var perNote = MatchSamplesToNotes(samples, notes);

        double pitch = ComputePitchAccuracy(perNote, opt);
        double stability = ComputeStability(perNote, opt);
        double longTone = ComputeLongTone(perNote, opt);
        double expression = ComputeExpression(samples, opt);

        int vibrato = CountVibrato(perNote, opt);
        int shakuri = CountShakuri(perNote, opt);
        int kobushi = CountKobushi(perNote, opt);

        double overall = ComputeOverall(
            pitch, stability, longTone, expression, vibrato, shakuri, kobushi, opt);

        return new ScoreResult
        {
            Overall = overall,
            PitchAccuracy = pitch,
            Stability = stability,
            LongTone = longTone,
            Expression = expression,
            VibratoCount = vibrato,
            ShakuriCount = shakuri,
            KobushiCount = kobushi,
        };
    }

    // ---------------------------------------------------------------------------------------
    // Matching
    // ---------------------------------------------------------------------------------------

    /// <summary>The voiced samples of one reference note, with that note, for per-note metrics.</summary>
    private readonly struct NoteSamples
    {
        public NoteSamples(ReferenceNote note, IReadOnlyList<PitchSample> voiced)
        {
            Note = note;
            Voiced = voiced;
        }

        public ReferenceNote Note { get; }

        /// <summary>The voiced samples whose time lies in <c>[Note.Start, Note.End)</c>, time-ordered.</summary>
        public IReadOnlyList<PitchSample> Voiced { get; }
    }

    private static NoteSamples[] MatchSamplesToNotes(PitchSample[] samples, ReferenceNote[] notes)
    {
        var result = new NoteSamples[notes.Length];
        for (int i = 0; i < notes.Length; i++)
        {
            ReferenceNote note = notes[i];
            var voiced = new List<PitchSample>();
            foreach (PitchSample s in samples)
            {
                if (s.Time < note.Start)
                {
                    continue;
                }

                if (s.Time >= note.End)
                {
                    continue;
                }

                if (s.IsVoiced && s.Frequency > 0.0)
                {
                    voiced.Add(s);
                }
            }

            result[i] = new NoteSamples(note, voiced);
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------
    // Pitch accuracy (音程)
    // ---------------------------------------------------------------------------------------

    private static double ComputePitchAccuracy(NoteSamples[] perNote, ScoringOptions opt)
    {
        // Time-weight each voiced sample by the hop spacing inside its note, so dense and sparse
        // sampling are treated equally and longer correct stretches count for more.
        double onPitchTime = 0.0;
        double totalTime = 0.0;

        foreach (NoteSamples ns in perNote)
        {
            IReadOnlyList<PitchSample> voiced = ns.Voiced;
            if (voiced.Count == 0)
            {
                continue;
            }

            for (int i = 0; i < voiced.Count; i++)
            {
                double weight = SampleWeightSeconds(voiced, i, ns.Note);
                totalTime += weight;
                double cents = Math.Abs(FoldedCents(voiced[i].Frequency, ns.Note.MidiNote, opt));
                if (cents <= opt.PitchToleranceCents)
                {
                    onPitchTime += weight;
                }
            }
        }

        if (totalTime <= 0.0)
        {
            return 0.0;
        }

        return Clamp01(onPitchTime / totalTime) * 100.0;
    }

    // ---------------------------------------------------------------------------------------
    // Stability (安定性)
    // ---------------------------------------------------------------------------------------

    private static double ComputeStability(NoteSamples[] perNote, ScoringOptions opt)
    {
        // Judge only "sustained" notes: those with enough voiced samples to have a meaningful
        // pitch trajectory. Average per-note cents standard deviation, then map to 0-100.
        double weightedStdSum = 0.0;
        double weightSum = 0.0;

        foreach (NoteSamples ns in perNote)
        {
            IReadOnlyList<PitchSample> voiced = ns.Voiced;
            if (voiced.Count < 4)
            {
                continue;
            }

            // Measure deviation of a smoothed pitch trajectory around its own mean. Smoothing with a
            // short moving average removes deliberate fast vibrato (which is an expressive technique,
            // not instability) while still exposing slow pitch wander, which is what "unstable" means.
            double std = SmoothedCentsStdDev(voiced, ns.Note, opt);
            double weight = ns.Note.Duration.TotalSeconds;
            weightedStdSum += std * weight;
            weightSum += weight;
        }

        if (weightSum <= 0.0)
        {
            return NeutralScore;
        }

        double meanStd = weightedStdSum / weightSum;

        // 0 cents std -> 100; ~60 cents std -> 0. Linear, clamped.
        const double zeroPointCents = 60.0;
        double score = (1.0 - meanStd / zeroPointCents) * 100.0;
        return Math.Clamp(score, 0.0, 100.0);
    }

    // ---------------------------------------------------------------------------------------
    // Long tone (ロングトーン)
    // ---------------------------------------------------------------------------------------

    private static double ComputeLongTone(NoteSamples[] perNote, ScoringOptions opt)
    {
        double weightedScoreSum = 0.0;
        double weightSum = 0.0;

        foreach (NoteSamples ns in perNote)
        {
            if (ns.Note.Duration.TotalSeconds < opt.LongToneMinSeconds)
            {
                continue;
            }

            IReadOnlyList<PitchSample> voiced = ns.Voiced;
            double weight = ns.Note.Duration.TotalSeconds;
            weightSum += weight;

            if (voiced.Count < 2)
            {
                // A long note with (almost) no sung voicing scores zero for that note.
                continue;
            }

            // Coverage: how much of the note was actually sung (voiced span / note span).
            double covered = (voiced[^1].Time - voiced[0].Time).TotalSeconds;
            double coverage = Clamp01(covered / ns.Note.Duration.TotalSeconds);

            // On-pitch fraction across the note.
            int onPitch = 0;
            foreach (PitchSample s in voiced)
            {
                if (Math.Abs(FoldedCents(s.Frequency, ns.Note.MidiNote, opt)) <= opt.PitchToleranceCents)
                {
                    onPitch++;
                }
            }

            double onPitchFraction = (double)onPitch / voiced.Count;

            // Steadiness: lower std -> higher. Use the vibrato-smoothed trajectory so a long note
            // decorated with deliberate vibrato is not punished as unsteady.
            double std = SmoothedCentsStdDev(voiced, ns.Note, opt);
            double steadiness = Math.Clamp(1.0 - std / 50.0, 0.0, 1.0);

            double noteScore = coverage * onPitchFraction * steadiness * 100.0;
            weightedScoreSum += noteScore * weight;
        }

        if (weightSum <= 0.0)
        {
            // No long notes in this song: neutral, don't penalise.
            return NeutralScore;
        }

        return Math.Clamp(weightedScoreSum / weightSum, 0.0, 100.0);
    }

    // ---------------------------------------------------------------------------------------
    // Expression (抑揚)
    // ---------------------------------------------------------------------------------------

    private static double ComputeExpression(PitchSample[] samples, ScoringOptions opt)
    {
        // Only voiced samples carry meaningful loudness for expression.
        var energies = new List<double>();
        foreach (PitchSample s in samples)
        {
            if (s.IsVoiced)
            {
                energies.Add(s.Energy);
            }
        }

        if (energies.Count == 0)
        {
            return NeutralScore;
        }

        double maxEnergy = 0.0;
        foreach (double e in energies)
        {
            if (e > maxEnergy)
            {
                maxEnergy = e;
            }
        }

        // All-zero energy means loudness is unknown: return a NEUTRAL score, do not penalise.
        if (maxEnergy <= 0.0)
        {
            return NeutralScore;
        }

        double mean = energies.Average();
        double variance = 0.0;
        foreach (double e in energies)
        {
            double d = e - mean;
            variance += d * d;
        }

        variance /= energies.Count;
        double std = Math.Sqrt(variance);

        // Reward dynamic variation. A std-dev of ~0.2 (on a 0..1 energy scale) is treated as full
        // marks; a perfectly flat dynamic earns a modest floor rather than zero.
        const double fullMarksStd = 0.2;
        const double floor = 40.0;
        double dynamic = Math.Clamp(std / fullMarksStd, 0.0, 1.0);
        return floor + (100.0 - floor) * dynamic;
    }

    // ---------------------------------------------------------------------------------------
    // Vibrato (ビブラート)
    // ---------------------------------------------------------------------------------------

    private static int CountVibrato(NoteSamples[] perNote, ScoringOptions opt)
    {
        int count = 0;
        foreach (NoteSamples ns in perNote)
        {
            if (ns.Note.Duration.TotalSeconds < opt.LongToneMinSeconds)
            {
                continue;
            }

            if (HasVibrato(ns.Voiced, opt))
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasVibrato(IReadOnlyList<PitchSample> voiced, ScoringOptions opt)
    {
        if (voiced.Count < 6)
        {
            return false;
        }

        // Build a cents trajectory relative to the note's mean pitch, then estimate the dominant
        // modulation rate from zero-crossings of the detrended signal and its peak-to-peak depth.
        int n = voiced.Count;
        var cents = new double[n];
        double mean = 0.0;
        for (int i = 0; i < n; i++)
        {
            cents[i] = MusicMath.FrequencyToMidiNote(voiced[i].Frequency) * 100.0;
            mean += cents[i];
        }

        mean /= n;

        double min = double.MaxValue;
        double max = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            cents[i] -= mean;
            if (cents[i] < min)
            {
                min = cents[i];
            }

            if (cents[i] > max)
            {
                max = cents[i];
            }
        }

        double depth = max - min;
        if (depth < opt.VibratoMinDepthCents)
        {
            return false;
        }

        // Count upward zero-crossings of the centred signal; each full cycle has one. The total
        // voiced span gives us the duration to convert to a rate in Hz.
        double span = (voiced[^1].Time - voiced[0].Time).TotalSeconds;
        if (span <= 0.0)
        {
            return false;
        }

        int upCrossings = 0;
        for (int i = 1; i < n; i++)
        {
            if (cents[i - 1] <= 0.0 && cents[i] > 0.0)
            {
                upCrossings++;
            }
        }

        if (upCrossings == 0)
        {
            return false;
        }

        double rateHz = upCrossings / span;
        return rateHz >= opt.VibratoMinRateHz && rateHz <= opt.VibratoMaxRateHz;
    }

    // ---------------------------------------------------------------------------------------
    // Shakuri (しゃくり) — onset rising from below into the target
    // ---------------------------------------------------------------------------------------

    private static int CountShakuri(NoteSamples[] perNote, ScoringOptions opt)
    {
        int count = 0;
        foreach (NoteSamples ns in perNote)
        {
            IReadOnlyList<PitchSample> voiced = ns.Voiced;
            if (voiced.Count < 3)
            {
                continue;
            }

            TimeSpan onsetEnd = ns.Note.Start + TimeSpan.FromSeconds(opt.ShakuriOnsetSeconds);

            // First sample within the onset window.
            PitchSample first = voiced[0];
            if (first.Time >= onsetEnd)
            {
                continue;
            }

            double firstCents = FoldedCents(first.Frequency, ns.Note.MidiNote, opt);

            // The onset must start clearly below the target.
            if (firstCents > -opt.ShakuriMinDepthCents)
            {
                continue;
            }

            // Within the onset window the pitch must rise up to the target (within tolerance),
            // monotonically enough that it's a slide, not a wobble.
            bool reachedTarget = false;
            bool rising = true;
            double prev = firstCents;
            foreach (PitchSample s in voiced)
            {
                if (s.Time >= onsetEnd)
                {
                    break;
                }

                double c = FoldedCents(s.Frequency, ns.Note.MidiNote, opt);
                if (c < prev - opt.PitchToleranceCents)
                {
                    // A meaningful dip breaks the slide.
                    rising = false;
                    break;
                }

                prev = Math.Max(prev, c);
                if (Math.Abs(c) <= opt.PitchToleranceCents)
                {
                    reachedTarget = true;
                }
            }

            if (rising && reachedTarget)
            {
                count++;
            }
        }

        return count;
    }

    // ---------------------------------------------------------------------------------------
    // Kobushi (こぶし) — brief up-and-return bend within a held note
    // ---------------------------------------------------------------------------------------

    private static int CountKobushi(NoteSamples[] perNote, ScoringOptions opt)
    {
        int count = 0;
        foreach (NoteSamples ns in perNote)
        {
            if (ns.Note.Duration.TotalSeconds < opt.LongToneMinSeconds)
            {
                continue;
            }

            if (HasKobushi(ns.Voiced, opt))
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasKobushi(IReadOnlyList<PitchSample> voiced, ScoringOptions opt)
    {
        int n = voiced.Count;
        if (n < 5)
        {
            return false;
        }

        var cents = new double[n];
        for (int i = 0; i < n; i++)
        {
            cents[i] = MusicMath.FrequencyToMidiNote(voiced[i].Frequency) * 100.0;
        }

        // Baseline of the held note: the median cents value (robust to a brief excursion).
        var sorted = (double[])cents.Clone();
        Array.Sort(sorted);
        double baseline = sorted[n / 2];

        // Skip the onset and tail so a shakuri-style entry is not mistaken for kobushi. Look for a
        // single up-and-back excursion within the held middle: a span that rises clearly above the
        // baseline and returns to it. The baseline window flanking the peak must itself sit near the
        // baseline, so a step change to a new sustained pitch is not counted.
        int margin = Math.Max(2, n / 6);
        int startIdx = margin;
        int endIdx = n - 1 - margin;

        for (int i = startIdx; i <= endIdx; i++)
        {
            double rise = cents[i] - baseline;
            if (rise < opt.KobushiMinDepthCents)
            {
                continue;
            }

            // It must come back down: samples a short way before and after the peak are near baseline.
            double before = cents[i - margin];
            double after = cents[i + margin];
            bool returns =
                Math.Abs(before - baseline) <= opt.PitchToleranceCents &&
                Math.Abs(after - baseline) <= opt.PitchToleranceCents;

            if (returns)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------
    // Overall
    // ---------------------------------------------------------------------------------------

    private static double ComputeOverall(
        double pitch,
        double stability,
        double longTone,
        double expression,
        int vibrato,
        int shakuri,
        int kobushi,
        ScoringOptions opt)
    {
        double weightSum = opt.PitchWeight + opt.StabilityWeight + opt.LongToneWeight + opt.ExpressionWeight;
        double baseScore;
        if (weightSum <= 0.0)
        {
            baseScore = pitch;
        }
        else
        {
            baseScore =
                (pitch * opt.PitchWeight
                 + stability * opt.StabilityWeight
                 + longTone * opt.LongToneWeight
                 + expression * opt.ExpressionWeight)
                / weightSum;
        }

        double vibratoBonus = Math.Min(vibrato * opt.VibratoBonusPerCount, opt.VibratoBonusCap);
        double shakuriBonus = Math.Min(shakuri * opt.ShakuriBonusPerCount, opt.ShakuriBonusCap);
        double kobushiBonus = Math.Min(kobushi * opt.KobushiBonusPerCount, opt.KobushiBonusCap);

        double overall = baseScore + vibratoBonus + shakuriBonus + kobushiBonus;
        return Math.Clamp(overall, 0.0, 100.0);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Signed cents error of a sung frequency relative to a target MIDI note, octave-folded into
    /// <c>[-600, 600]</c> when <see cref="ScoringOptions.OctaveAgnostic"/> is set.
    /// </summary>
    private static double FoldedCents(double sungHz, int targetMidiNote, ScoringOptions opt)
    {
        double cents = MusicMath.CentsError(sungHz, targetMidiNote);
        if (!opt.OctaveAgnostic)
        {
            return cents;
        }

        // Fold into (-600, 600] so an octave (1200 cents) off becomes 0.
        cents %= 1200.0;
        if (cents > 600.0)
        {
            cents -= 1200.0;
        }
        else if (cents <= -600.0)
        {
            cents += 1200.0;
        }

        return cents;
    }

    /// <summary>
    /// Standard deviation, in cents, of a short moving-average of the note's folded-cents trajectory.
    /// The smoothing window removes deliberate vibrato (fast oscillation) so it does not register as
    /// instability, while preserving slow pitch wander.
    /// </summary>
    private static double SmoothedCentsStdDev(IReadOnlyList<PitchSample> voiced, ReferenceNote note, ScoringOptions opt)
    {
        int n = voiced.Count;
        if (n < 2)
        {
            return 0.0;
        }

        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = FoldedCents(voiced[i].Frequency, note.MidiNote, opt);
        }

        // Smooth with a moving average whose window spans about one full cycle of the slowest
        // allowed vibrato, so any vibrato in the configured band averages out and does not register
        // as instability. Slower (sub-vibrato) wander survives the smoothing and is still penalised.
        int half = VibratoSmoothingHalfWindow(voiced, opt);
        var smoothed = new double[n];
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - half);
            int hi = Math.Min(n - 1, i + half);
            double sum = 0.0;
            for (int j = lo; j <= hi; j++)
            {
                sum += values[j];
            }

            smoothed[i] = sum / (hi - lo + 1);
        }

        double mean = 0.0;
        for (int i = 0; i < n; i++)
        {
            mean += smoothed[i];
        }

        mean /= n;

        double variance = 0.0;
        for (int i = 0; i < n; i++)
        {
            double d = smoothed[i] - mean;
            variance += d * d;
        }

        variance /= n;
        return Math.Sqrt(variance);
    }

    /// <summary>
    /// Half-window (in samples) for vibrato-removing smoothing: about half the period of the slowest
    /// vibrato rate, derived from the note's median hop spacing. At least 1.
    /// </summary>
    private static int VibratoSmoothingHalfWindow(IReadOnlyList<PitchSample> voiced, ScoringOptions opt)
    {
        int n = voiced.Count;
        double span = (voiced[^1].Time - voiced[0].Time).TotalSeconds;
        if (span <= 0.0 || n < 2)
        {
            return 1;
        }

        double hop = span / (n - 1);
        double slowestPeriod = opt.VibratoMinRateHz > 0.0 ? 1.0 / opt.VibratoMinRateHz : 0.25;
        int half = (int)Math.Round(slowestPeriod / 2.0 / hop);
        return Math.Max(1, half);
    }

    /// <summary>
    /// Time weight (seconds) attributed to a voiced sample: half the gap to each neighbour, clamped
    /// to the note's bounds. This makes the pitch metric independent of hop density.
    /// </summary>
    private static double SampleWeightSeconds(IReadOnlyList<PitchSample> voiced, int index, ReferenceNote note)
    {
        int n = voiced.Count;
        if (n == 1)
        {
            return Math.Max(note.Duration.TotalSeconds, 1e-6);
        }

        double t = voiced[index].Time.TotalSeconds;
        double left = index > 0 ? voiced[index - 1].Time.TotalSeconds : note.Start.TotalSeconds;
        double right = index < n - 1 ? voiced[index + 1].Time.TotalSeconds : note.End.TotalSeconds;

        double half = (t - left) / 2.0 + (right - t) / 2.0;
        return Math.Max(half, 1e-6);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);
}
