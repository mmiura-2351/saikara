using Saikara.Core.Pitch;

namespace Saikara.Core.Tests.Pitch;

public class PitchResultTests
{
    [Fact]
    public void Unvoiced_HasZeroFrequencyAndIsNotVoiced()
    {
        PitchResult r = PitchResult.Unvoiced;

        Assert.False(r.IsVoiced);
        Assert.Equal(0.0, r.Frequency);
        Assert.Equal(0.0, r.Clarity);
    }

    [Fact]
    public void Voiced_FactoryCarriesFrequencyAndClarity_AndIsVoiced()
    {
        PitchResult r = PitchResult.Voiced(440.0, 0.95);

        Assert.True(r.IsVoiced);
        Assert.Equal(440.0, r.Frequency);
        Assert.Equal(0.95, r.Clarity);
    }

    [Fact]
    public void ToMidiNote_OnVoiced_MatchesMusicMath()
    {
        PitchResult r = PitchResult.Voiced(440.0, 0.99);

        double? midi = r.ToMidiNote();

        Assert.NotNull(midi);
        Assert.Equal(69.0, midi!.Value, precision: 6);
    }

    [Fact]
    public void ToMidiNote_OnUnvoiced_IsNull()
    {
        Assert.Null(PitchResult.Unvoiced.ToMidiNote());
    }
}
