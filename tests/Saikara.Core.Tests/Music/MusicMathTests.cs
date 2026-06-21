using Saikara.Core.Music;

namespace Saikara.Core.Tests.Music;

public class MusicMathTests
{
    [Theory]
    [InlineData(69, 440.0)]    // A4
    [InlineData(60, 261.6256)] // C4 (middle C)
    [InlineData(81, 880.0)]    // A5
    [InlineData(57, 220.0)]    // A3
    public void MidiNoteToFrequency_MatchesEqualTemperament(int midiNote, double expectedHz)
    {
        double hz = MusicMath.MidiNoteToFrequency(midiNote);
        Assert.Equal(expectedHz, hz, precision: 3);
    }

    [Fact]
    public void FrequencyToMidiNote_IsInverseOfMidiNoteToFrequency()
    {
        for (int note = 21; note <= 108; note++) // piano range A0..C8
        {
            double hz = MusicMath.MidiNoteToFrequency(note);
            double roundTrip = MusicMath.FrequencyToMidiNote(hz);
            Assert.Equal(note, roundTrip, precision: 6);
        }
    }

    [Fact]
    public void FrequencyToMidiNote_RejectsNonPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MusicMath.FrequencyToMidiNote(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MusicMath.FrequencyToMidiNote(-100));
    }

    [Fact]
    public void CentsError_IsZeroOnTarget_AndSignedOffTarget()
    {
        double onPitch = MusicMath.MidiNoteToFrequency(69);
        Assert.Equal(0.0, MusicMath.CentsError(onPitch, 69), precision: 6);

        // A quarter-tone sharp == +50 cents.
        double quarterSharp = MusicMath.MidiNoteToFrequency(69.5);
        Assert.Equal(50.0, MusicMath.CentsError(quarterSharp, 69), precision: 3);
    }

    [Theory]
    [InlineData(60, 7, 67)]    // C4 up a fifth -> G4
    [InlineData(60, -12, 48)]  // down an octave
    [InlineData(125, 10, 127)] // clamps at the top
    [InlineData(2, -10, 0)]    // clamps at the bottom
    public void Transpose_ShiftsAndClamps(int note, int semitones, int expected)
        => Assert.Equal(expected, MusicMath.Transpose(note, semitones));

    [Theory]
    [InlineData(60, "C4")]
    [InlineData(69, "A4")]
    [InlineData(61, "C#4")]
    [InlineData(21, "A0")]
    public void NoteName_UsesScientificPitchNotation(int note, string expected)
        => Assert.Equal(expected, MusicMath.NoteName(note));
}
