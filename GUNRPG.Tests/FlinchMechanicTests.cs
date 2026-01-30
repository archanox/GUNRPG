using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

public class FlinchMechanicTests
{
    [Fact]
    public void FlinchSeverity_HighResistance_ReducesSeverity()
    {
        float incomingImpulse = 1.0f;
        float lowResistance = 0.1f;
        float highResistance = 0.2f;

        float lowSeverity = AccuracyModel.CalculateFlinchSeverity(incomingImpulse, lowResistance);
        float highSeverity = AccuracyModel.CalculateFlinchSeverity(incomingImpulse, highResistance);

        Assert.True(highSeverity < lowSeverity,
            $"High resistance severity ({highSeverity:F3}) should be lower than low resistance ({lowSeverity:F3})");
    }

    [Fact]
    public void FlinchSeverity_LowResistance_AmplifiesSeverity()
    {
        float incomingImpulse = 1.0f;
        float baselineResistance = 0.2f;
        float lowResistance = 0.05f;

        float baselineSeverity = AccuracyModel.CalculateFlinchSeverity(incomingImpulse, baselineResistance);
        float lowSeverity = AccuracyModel.CalculateFlinchSeverity(incomingImpulse, lowResistance);

        Assert.True(lowSeverity > baselineSeverity,
            $"Low resistance severity ({lowSeverity:F3}) should be higher than baseline ({baselineSeverity:F3})");
    }

    [Fact]
    public void EffectiveAccuracyProficiency_RespectsFloor()
    {
        float baseProficiency = 0.6f;
        float flinchSeverity = 0.9f;

        float effective = AccuracyModel.CalculateEffectiveAccuracyProficiency(baseProficiency, flinchSeverity);

        float expectedFloor = baseProficiency * AccuracyModel.FlinchProficiencyFloorFactor;
        Assert.Equal(expectedFloor, effective, 3);
    }

    [Fact]
    public void FlinchClears_AfterDurationShots()
    {
        var op = new Operator("Test")
        {
            FlinchDurationShots = 1
        };

        op.ApplyFlinch(0.5f, currentTimeMs: 0);

        Assert.Equal(1, op.FlinchShotsRemaining);
        Assert.True(op.FlinchSeverity > 0f);

        op.ConsumeFlinchShot();

        Assert.Equal(0, op.FlinchShotsRemaining);
        Assert.Equal(0f, op.FlinchSeverity, 3);
    }
}
