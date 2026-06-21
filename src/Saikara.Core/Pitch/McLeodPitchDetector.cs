namespace Saikara.Core.Pitch;

/// <summary>
/// A monophonic pitch detector implementing the McLeod Pitch Method (MPM), as described in
/// Philip McLeod &amp; Geoff Wyvill, "A Smarter Way to Find Pitch" (ICMC 2005).
/// </summary>
/// <remarks>
/// <para>
/// MPM works on the Normalized Square Difference Function (NSDF) of a frame. For lag
/// <c>tau</c> the NSDF is
/// <c>n(tau) = 2 * autocorrelation(tau) / (m(tau))</c>, where
/// <c>m(tau)</c> is the sum of the squared values of the two overlapping windows. The NSDF
/// is bounded in <c>[-1, 1]</c>; a value near <c>1</c> at lag <c>tau</c> means the signal
/// repeats strongly with period <c>tau</c>. The algorithm then:
/// </para>
/// <list type="number">
/// <item>finds the "key maxima" — the highest local maximum between each pair of positive
/// zero crossings of the NSDF;</item>
/// <item>takes the largest key maximum as a reference and picks the <em>first</em> key
/// maximum whose height clears <c>k * (largest height)</c> (here a fixed fraction). Choosing
/// the first sufficiently-tall maximum, rather than the globally tallest, is what avoids
/// octave errors when an upper harmonic is louder than the fundamental;</item>
/// <item>refines that lag with parabolic interpolation over the three NSDF samples around the
/// chosen maximum for sub-sample accuracy, then reports
/// <c>frequency = sampleRate / interpolatedLag</c> and the interpolated height as clarity.</item>
/// </list>
/// <para>
/// A frame is reported as voiced only when the chosen maximum's height also clears
/// <see cref="PitchDetectorOptions.ClarityThreshold"/> and the resulting frequency lies within
/// <c>[MinFrequency, MaxFrequency]</c>; otherwise <see cref="PitchResult.Unvoiced"/> is
/// returned.
/// </para>
/// <para>
/// <b>Complexity.</b> The NSDF is computed directly as a double loop, so a frame of
/// <c>N</c> samples costs <c>O(N * maxLag)</c> time (with <c>maxLag</c> bounded by
/// <c>sampleRate / MinFrequency</c>) and only <c>O(N)</c> scratch memory, reused across calls
/// on the same instance. This is fine for the 2048–4096-sample frames the real-time pitch bar
/// uses; an FFT-based autocorrelation would be the upgrade path if larger frames are ever
/// needed. A <see cref="McLeodPitchDetector"/> instance is <b>not</b> thread-safe because it
/// reuses an internal NSDF buffer; use one per capture thread.
/// </para>
/// </remarks>
public sealed class McLeodPitchDetector : IPitchDetector
{
    /// <summary>
    /// Fraction of the largest key-maximum height that a key maximum must reach to be picked
    /// (the "first sufficiently tall" rule). McLeod suggests roughly 0.8–0.9; 0.9 is used here.
    /// </summary>
    private const double PeakPickThreshold = 0.9;

    private readonly double _minFrequency;
    private readonly double _maxFrequency;
    private readonly double _clarityThreshold;

    /// <summary>Reused scratch buffer holding the NSDF for the current frame.</summary>
    private double[] _nsdf = Array.Empty<double>();

    /// <summary>Creates a detector with the default options (≈70–1100 Hz, clarity 0.9).</summary>
    public McLeodPitchDetector() : this(new PitchDetectorOptions())
    {
    }

    /// <summary>Creates a detector with explicit options.</summary>
    /// <param name="options">The detection parameters; validated on construction.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the frequency range is non-positive or inverted, or the clarity threshold
    /// is outside <c>[0, 1]</c>.
    /// </exception>
    public McLeodPitchDetector(PitchDetectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MinFrequency <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(options), options.MinFrequency,
                "MinFrequency must be positive.");
        if (options.MaxFrequency <= options.MinFrequency)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxFrequency,
                "MaxFrequency must be greater than MinFrequency.");
        if (options.ClarityThreshold is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(options), options.ClarityThreshold,
                "ClarityThreshold must be in [0, 1].");

        _minFrequency = options.MinFrequency;
        _maxFrequency = options.MaxFrequency;
        _clarityThreshold = options.ClarityThreshold;
    }

    /// <inheritdoc />
    public PitchResult Detect(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");

        int n = samples.Length;

        // The longest period we look for corresponds to the lowest detectable frequency. We
        // need the frame to be longer than that period to have any overlap to correlate.
        int maxLag = (int)(sampleRate / _minFrequency) + 1;
        int minLag = Math.Max(1, (int)(sampleRate / _maxFrequency));
        if (maxLag >= n)
            maxLag = n - 1;
        if (maxLag < minLag || maxLag < 2)
            return PitchResult.Unvoiced;

        // Ensure the NSDF scratch buffer is big enough (grow-only; reused across calls).
        if (_nsdf.Length < maxLag + 1)
            _nsdf = new double[maxLag + 1];
        double[] nsdf = _nsdf;

        // Compute the NSDF from lag 0 so that the band below minLag is available too: a tone
        // whose true fundamental is above MaxFrequency shows a tall NSDF peak at a lag shorter
        // than minLag, while only sub-octave ghosts appear in [minLag, maxLag]. Detecting that
        // out-of-range peak lets us reject the frame instead of reporting a sub-octave.
        ComputeNsdf(samples, nsdf, maxLag);

        // Reject when the real fundamental lies above the ceiling: a key maximum below minLag
        // that is at least as tall as everything in range means we'd otherwise lock onto a
        // sub-multiple. Use the same clarity bar so genuine in-range tones with weak low-lag
        // ripple are unaffected.
        for (int tau = 1; tau < minLag; tau++)
        {
            if (nsdf[tau] >= _clarityThreshold &&
                nsdf[tau] > nsdf[tau - 1] && nsdf[tau] >= nsdf[Math.Min(tau + 1, maxLag)])
            {
                return PitchResult.Unvoiced;
            }
        }

        // --- Pick the period via the key-maxima rule. ---
        double maxNsdf = 0.0;
        // First pass: locate every key maximum and remember the tallest, so we can set the
        // peak-pick threshold relative to it.
        for (int tau = minLag; tau < maxLag; tau++)
        {
            if (nsdf[tau] > maxNsdf)
                maxNsdf = nsdf[tau];
        }

        if (maxNsdf <= 0.0)
            return PitchResult.Unvoiced;

        double pickThreshold = PeakPickThreshold * maxNsdf;

        // Second pass: walk lags looking for key maxima (highest point between successive
        // positive-going zero crossings) and choose the first one that clears pickThreshold.
        int chosenLag = -1;
        double chosenValue = 0.0;

        int tauCur = minLag;
        while (tauCur < maxLag)
        {
            // Skip until the NSDF is positive (start of a candidate hump).
            if (nsdf[tauCur] <= 0.0)
            {
                tauCur++;
                continue;
            }

            // We are inside a positive region; track its peak until the NSDF goes non-positive.
            int peakLag = tauCur;
            double peakValue = nsdf[tauCur];
            int tau = tauCur + 1;
            while (tau < maxLag && nsdf[tau] > 0.0)
            {
                if (nsdf[tau] > peakValue)
                {
                    peakValue = nsdf[tau];
                    peakLag = tau;
                }
                tau++;
            }

            if (peakValue >= pickThreshold)
            {
                chosenLag = peakLag;
                chosenValue = peakValue;
                break;
            }

            // Advance past this positive region and keep searching.
            tauCur = tau;
        }

        if (chosenLag <= 0)
            return PitchResult.Unvoiced;

        // --- Sub-sample refinement via parabolic interpolation. ---
        (double refinedLag, double refinedValue) = ParabolicInterpolate(nsdf, chosenLag, maxLag);

        double clarity = Math.Clamp(refinedValue, 0.0, 1.0);
        if (clarity < _clarityThreshold)
            return PitchResult.Unvoiced;

        double frequency = sampleRate / refinedLag;
        if (frequency < _minFrequency || frequency > _maxFrequency)
            return PitchResult.Unvoiced;

        return PitchResult.Voiced(frequency, clarity);
    }

    /// <summary>
    /// Fills <paramref name="nsdf"/>[0..maxLag] with the Normalized Square Difference Function
    /// of <paramref name="samples"/>. <c>nsdf[tau] = 2 * r(tau) / m(tau)</c>, where
    /// <c>r(tau)</c> is the (type-II) autocorrelation at lag <c>tau</c> and <c>m(tau)</c> is
    /// the summed squared energy of the two overlapping windows. The result is in <c>[-1, 1]</c>.
    /// </summary>
    private static void ComputeNsdf(ReadOnlySpan<float> samples, double[] nsdf, int maxLag)
    {
        int n = samples.Length;
        for (int tau = 0; tau <= maxLag; tau++)
        {
            double acf = 0.0;     // sum x[i] * x[i + tau]
            double divisor = 0.0; // sum x[i]^2 + x[i + tau]^2
            int limit = n - tau;
            for (int i = 0; i < limit; i++)
            {
                double a = samples[i];
                double b = samples[i + tau];
                acf += a * b;
                divisor += a * a + b * b;
            }

            nsdf[tau] = divisor > 0.0 ? 2.0 * acf / divisor : 0.0;
        }
    }

    /// <summary>
    /// Refines the integer-lag peak at <paramref name="lag"/> using a parabola fitted through
    /// the three NSDF samples around it, returning the interpolated lag and peak height. Falls
    /// back to the integer lag at the buffer edges where neighbours are unavailable.
    /// </summary>
    private static (double lag, double value) ParabolicInterpolate(double[] nsdf, int lag, int maxLag)
    {
        if (lag <= 0 || lag >= maxLag)
            return (lag, nsdf[lag]);

        double y0 = nsdf[lag - 1];
        double y1 = nsdf[lag];
        double y2 = nsdf[lag + 1];

        double denom = y0 - 2.0 * y1 + y2;
        if (Math.Abs(denom) < 1e-12)
            return (lag, y1);

        // Vertex of the parabola through (-1,y0),(0,y1),(1,y2), measured from the integer lag.
        double delta = 0.5 * (y0 - y2) / denom;
        // Guard against numerical blow-ups: a real peak's vertex sits within ±1 sample.
        if (delta is < -1.0 or > 1.0)
            return (lag, y1);

        double refinedLag = lag + delta;
        double refinedValue = y1 - 0.25 * (y0 - y2) * delta;
        return (refinedLag, refinedValue);
    }
}
