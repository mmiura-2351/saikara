using Saikara.Core.Music;
using Saikara.Core.Pitch;

namespace Saikara.Core.Tests.Pitch;

public class McLeodPitchDetectorTests
{
    private const int SampleRate44 = 44100;
    private const int SampleRate48 = 48000;
    private const int FrameSize = 4096;

    /// <summary>Synthesizes a pure cosine wave of the given frequency.</summary>
    private static float[] Sine(double freq, int sampleRate, int length, double amplitude = 0.8, double phase = 0.0)
    {
        var buffer = new float[length];
        double w = 2.0 * Math.PI * freq / sampleRate;
        for (int i = 0; i < length; i++)
            buffer[i] = (float)(amplitude * Math.Sin(w * i + phase));
        return buffer;
    }

    /// <summary>Sums several harmonics with the given amplitudes (index 0 == fundamental).</summary>
    private static float[] Harmonics(double fundamental, int sampleRate, int length, params double[] amplitudes)
    {
        var buffer = new float[length];
        for (int h = 0; h < amplitudes.Length; h++)
        {
            double w = 2.0 * Math.PI * fundamental * (h + 1) / sampleRate;
            for (int i = 0; i < length; i++)
                buffer[i] += (float)(amplitudes[h] * Math.Sin(w * i));
        }
        return buffer;
    }

    private static double Cents(double detectedHz, double trueHz)
        => 1200.0 * Math.Log2(detectedHz / trueHz);

    [Theory]
    [InlineData(110.0)]
    [InlineData(220.0)]
    [InlineData(261.63)]
    [InlineData(440.0)]
    [InlineData(880.0)]
    public void Detect_PureSine44100_WithinAFewCents(double freq)
    {
        var detector = new McLeodPitchDetector();
        float[] frame = Sine(freq, SampleRate44, FrameSize);

        PitchResult r = detector.Detect(frame, SampleRate44);

        Assert.True(r.IsVoiced, $"expected voiced for {freq} Hz");
        Assert.True(Math.Abs(Cents(r.Frequency, freq)) < 5.0,
            $"{freq} Hz detected as {r.Frequency} Hz ({Cents(r.Frequency, freq):F2} cents off)");
    }

    [Theory]
    [InlineData(220.0)]
    [InlineData(440.0)]
    public void Detect_PureSine48000_WithinAFewCents(double freq)
    {
        var detector = new McLeodPitchDetector();
        float[] frame = Sine(freq, SampleRate48, FrameSize);

        PitchResult r = detector.Detect(frame, SampleRate48);

        Assert.True(r.IsVoiced);
        Assert.True(Math.Abs(Cents(r.Frequency, freq)) < 5.0,
            $"{freq} Hz detected as {r.Frequency} Hz ({Cents(r.Frequency, freq):F2} cents off)");
    }

    [Fact]
    public void Detect_PureTone_HasHighClarity()
    {
        var detector = new McLeodPitchDetector();
        float[] frame = Sine(440.0, SampleRate44, FrameSize);

        PitchResult r = detector.Detect(frame, SampleRate44);

        Assert.True(r.IsVoiced);
        Assert.True(r.Clarity > 0.9, $"clarity was {r.Clarity}");
        Assert.True(r.Clarity <= 1.0 + 1e-9, $"clarity must not exceed 1, was {r.Clarity}");
    }

    [Fact]
    public void Detect_WhiteNoise_IsUnvoiced()
    {
        var detector = new McLeodPitchDetector();
        var rng = new Random(12345);
        var frame = new float[FrameSize];
        for (int i = 0; i < frame.Length; i++)
            frame[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        PitchResult r = detector.Detect(frame, SampleRate44);

        Assert.False(r.IsVoiced, $"white noise should be unvoiced (clarity {r.Clarity}, freq {r.Frequency})");
    }

    [Fact]
    public void Detect_Silence_IsUnvoiced()
    {
        var detector = new McLeodPitchDetector();
        var frame = new float[FrameSize]; // all zeros

        PitchResult r = detector.Detect(frame, SampleRate44);

        Assert.Equal(PitchResult.Unvoiced, r);
        Assert.False(r.IsVoiced);
    }

    [Fact]
    public void Detect_FundamentalPlusWeakerHarmonics_FindsFundamentalNoOctaveError()
    {
        var detector = new McLeodPitchDetector();
        // 220 Hz fundamental with decaying 2nd and 3rd harmonics.
        float[] frame = Harmonics(220.0, SampleRate44, FrameSize, 1.0, 0.5, 0.3);

        PitchResult r = detector.Detect(frame, SampleRate44);

        Assert.True(r.IsVoiced);
        Assert.True(Math.Abs(Cents(r.Frequency, 220.0)) < 10.0,
            $"expected ~220 Hz, got {r.Frequency} Hz ({Cents(r.Frequency, 220.0):F2} cents)");
    }

    [Fact]
    public void Detect_RichHarmonics_StillNoOctaveError()
    {
        var detector = new McLeodPitchDetector();
        // A stronger 2nd harmonic than fundamental would trip a naive autocorrelation peak picker.
        float[] frame = Harmonics(146.83 /* D3 */, SampleRate44, FrameSize, 0.6, 0.8, 0.4, 0.2);

        PitchResult r = detector.Detect(frame, SampleRate44);

        Assert.True(r.IsVoiced);
        Assert.True(Math.Abs(Cents(r.Frequency, 146.83)) < 15.0,
            $"expected ~146.83 Hz, got {r.Frequency} Hz ({Cents(r.Frequency, 146.83):F2} cents)");
    }

    [Fact]
    public void Detect_InRangeNearMinBoundary_IsVoiced()
    {
        var options = new PitchDetectorOptions { MinFrequency = 100.0, MaxFrequency = 1000.0 };
        var detector = new McLeodPitchDetector(options);
        float[] frame = Sine(110.0, SampleRate44, FrameSize);

        PitchResult r = detector.Detect(frame, SampleRate44);

        Assert.True(r.IsVoiced);
        Assert.True(Math.Abs(Cents(r.Frequency, 110.0)) < 10.0);
    }

    [Fact]
    public void Detect_BelowMinFrequency_IsUnvoiced()
    {
        var options = new PitchDetectorOptions { MinFrequency = 100.0, MaxFrequency = 1000.0 };
        var detector = new McLeodPitchDetector(options);
        float[] frame = Sine(80.0, SampleRate44, FrameSize);

        PitchResult r = detector.Detect(frame, SampleRate44);

        Assert.False(r.IsVoiced, $"80 Hz is below the 100 Hz floor but was detected as {r.Frequency} Hz");
    }

    [Fact]
    public void Detect_AboveMaxFrequency_IsUnvoiced()
    {
        var options = new PitchDetectorOptions { MinFrequency = 100.0, MaxFrequency = 800.0 };
        var detector = new McLeodPitchDetector(options);
        float[] frame = Sine(1000.0, SampleRate44, FrameSize);

        PitchResult r = detector.Detect(frame, SampleRate44);

        Assert.False(r.IsVoiced, $"1000 Hz is above the 800 Hz ceiling but was detected as {r.Frequency} Hz");
    }

    [Fact]
    public void Detect_ParabolicInterpolation_BeatsNearestIntegerLag()
    {
        var detector = new McLeodPitchDetector();
        // Pick a frequency whose period is decidedly non-integer in samples:
        // 44100 / 443.7 = 99.39... samples.
        const double freq = 443.7;
        float[] frame = Sine(freq, SampleRate44, FrameSize);

        PitchResult r = detector.Detect(frame, SampleRate44);
        Assert.True(r.IsVoiced);

        // The frequency one would get from the nearest integer lag (no interpolation).
        double integerLag = Math.Round(SampleRate44 / r.Frequency);
        double integerLagFreq = SampleRate44 / integerLag;

        double interpErr = Math.Abs(Cents(r.Frequency, freq));
        double integerErr = Math.Abs(Cents(integerLagFreq, freq));

        Assert.True(interpErr < integerErr,
            $"interpolated error {interpErr:F2} cents should beat integer-lag error {integerErr:F2} cents");
    }

    [Fact]
    public void Detect_ClarityThreshold_ConfigurableViaOptions()
    {
        // A very high threshold should reject a slightly noisy tone that a default detector accepts.
        var rng = new Random(7);
        float[] tone = Sine(330.0, SampleRate44, FrameSize, amplitude: 0.6);
        for (int i = 0; i < tone.Length; i++)
            tone[i] += (float)((rng.NextDouble() * 2.0 - 1.0) * 0.25);

        var lenient = new McLeodPitchDetector(new PitchDetectorOptions { ClarityThreshold = 0.8 });
        var strict = new McLeodPitchDetector(new PitchDetectorOptions { ClarityThreshold = 0.999 });

        Assert.True(lenient.Detect(tone, SampleRate44).IsVoiced);
        Assert.False(strict.Detect(tone, SampleRate44).IsVoiced);
    }

    [Fact]
    public void Detect_VoicedResult_RoundTripsThroughMusicMath()
    {
        var detector = new McLeodPitchDetector();
        float[] frame = Sine(440.0, SampleRate44, FrameSize);

        PitchResult r = detector.Detect(frame, SampleRate44);

        Assert.True(r.IsVoiced);
        double midi = MusicMath.FrequencyToMidiNote(r.Frequency);
        Assert.Equal(69.0, midi, precision: 1);
    }

    [Fact]
    public void Detect_RejectsNonPositiveSampleRate()
    {
        var detector = new McLeodPitchDetector();
        float[] frame = Sine(440.0, SampleRate44, FrameSize);

        Assert.Throws<ArgumentOutOfRangeException>(() => detector.Detect(frame, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => detector.Detect(frame, -44100));
    }

    [Fact]
    public void Detect_TooShortFrame_IsUnvoiced()
    {
        var detector = new McLeodPitchDetector();
        // A frame too short to hold even one period at the min frequency.
        var frame = new float[16];

        PitchResult r = detector.Detect(frame, SampleRate44);

        Assert.False(r.IsVoiced);
    }

    [Fact]
    public void Options_RejectInvalidRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new McLeodPitchDetector(new PitchDetectorOptions { MinFrequency = 500, MaxFrequency = 400 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new McLeodPitchDetector(new PitchDetectorOptions { ClarityThreshold = 1.5 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new McLeodPitchDetector(new PitchDetectorOptions { MinFrequency = 0 }));
    }
}
