using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Unit tests for the SuppressionModel and Operator suppression mechanics.
/// Tests suppression accumulation, decay, and effect calculations.
/// </summary>
public class SuppressionMechanicTests
{
    #region Suppression Severity Calculation

    [Fact]
    public void CalculateSuppressionSeverity_CloseNearMiss_ReturnsHighSeverity()
    {
        // LMG at close range with close miss
        float severity = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.5f,  // LMG
            weaponFireRateRPM: 600f,
            distanceMeters: 10f,            // Close range
            angularDeviationDegrees: 0.1f); // Close miss

        Assert.True(severity > 0.15f, $"Close LMG near-miss should cause significant suppression. Got: {severity}");
    }

    [Fact]
    public void CalculateSuppressionSeverity_FarMiss_ReturnsZeroSeverity()
    {
        // Shot that deviated too far from target
        float severity = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.5f,
            weaponFireRateRPM: 600f,
            distanceMeters: 10f,
            angularDeviationDegrees: 1.0f); // Beyond threshold

        Assert.Equal(0f, severity);
    }

    [Fact]
    public void CalculateSuppressionSeverity_LMGMoreSuppressiveThanSMG()
    {
        float lmgSeverity = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.5f,  // LMG
            weaponFireRateRPM: 600f,
            distanceMeters: 20f,
            angularDeviationDegrees: 0.2f);

        float smgSeverity = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 0.8f,  // SMG
            weaponFireRateRPM: 600f,
            distanceMeters: 20f,
            angularDeviationDegrees: 0.2f);

        Assert.True(lmgSeverity > smgSeverity,
            $"LMG ({lmgSeverity:F3}) should be more suppressive than SMG ({smgSeverity:F3})");
    }

    [Fact]
    public void CalculateSuppressionSeverity_CloserRangeMoreSuppressive()
    {
        float closeRangeSeverity = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.0f,
            weaponFireRateRPM: 600f,
            distanceMeters: 5f,
            angularDeviationDegrees: 0.2f);

        float farRangeSeverity = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.0f,
            weaponFireRateRPM: 600f,
            distanceMeters: 40f,
            angularDeviationDegrees: 0.2f);

        Assert.True(closeRangeSeverity > farRangeSeverity,
            $"Close range ({closeRangeSeverity:F3}) should be more suppressive than far range ({farRangeSeverity:F3})");
    }

    [Fact]
    public void CalculateSuppressionSeverity_CloserMissMoreSuppressive()
    {
        float closeMissSeverity = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.0f,
            weaponFireRateRPM: 600f,
            distanceMeters: 20f,
            angularDeviationDegrees: 0.1f);

        float farMissSeverity = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.0f,
            weaponFireRateRPM: 600f,
            distanceMeters: 20f,
            angularDeviationDegrees: 0.4f);

        Assert.True(closeMissSeverity > farMissSeverity,
            $"Close miss ({closeMissSeverity:F3}) should be more suppressive than far miss ({farMissSeverity:F3})");
    }

    [Fact]
    public void CalculateSuppressionSeverity_HigherFireRateMoreSuppressive()
    {
        float highRPMSeverity = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.0f,
            weaponFireRateRPM: 900f,  // High fire rate
            distanceMeters: 20f,
            angularDeviationDegrees: 0.2f);

        float lowRPMSeverity = SuppressionModel.CalculateSuppressionSeverity(
            weaponSuppressionFactor: 1.0f,
            weaponFireRateRPM: 400f,  // Low fire rate
            distanceMeters: 20f,
            angularDeviationDegrees: 0.2f);

        Assert.True(highRPMSeverity > lowRPMSeverity,
            $"High RPM ({highRPMSeverity:F3}) should be more suppressive than low RPM ({lowRPMSeverity:F3})");
    }

    #endregion

    #region Suppression Decay

    [Fact]
    public void ApplyDecay_DecaysOverTime()
    {
        float initial = 0.8f;
        float afterDecay = SuppressionModel.ApplyDecay(initial, deltaMs: 1000, isUnderFire: false);

        Assert.True(afterDecay < initial,
            $"Suppression should decay. Initial: {initial}, After: {afterDecay}");
    }

    [Fact]
    public void ApplyDecay_SlowerWhenUnderFire()
    {
        float initial = 0.8f;

        float normalDecay = SuppressionModel.ApplyDecay(initial, deltaMs: 1000, isUnderFire: false);
        float underFireDecay = SuppressionModel.ApplyDecay(initial, deltaMs: 1000, isUnderFire: true);

        Assert.True(underFireDecay > normalDecay,
            $"Decay under fire ({underFireDecay:F3}) should be slower than normal ({normalDecay:F3})");
    }

    [Fact]
    public void ApplyDecay_SnapsToZeroBelowThreshold()
    {
        float veryLow = 0.005f;
        float afterDecay = SuppressionModel.ApplyDecay(veryLow, deltaMs: 100, isUnderFire: false);

        Assert.Equal(0f, afterDecay);
    }

    [Fact]
    public void ApplyDecay_ZeroReturnsZero()
    {
        float afterDecay = SuppressionModel.ApplyDecay(0f, deltaMs: 1000, isUnderFire: false);
        Assert.Equal(0f, afterDecay);
    }

    #endregion

    #region Suppression Effects

    [Fact]
    public void CalculateEffectiveADSTime_IncreasesWithSuppression()
    {
        float baseADS = 200f;
        float noSuppressionADS = SuppressionModel.CalculateEffectiveADSTime(baseADS, 0f);
        float fullSuppressionADS = SuppressionModel.CalculateEffectiveADSTime(baseADS, 1f);

        Assert.Equal(baseADS, noSuppressionADS);
        Assert.True(fullSuppressionADS > baseADS,
            $"Full suppression ADS ({fullSuppressionADS}) should be greater than base ({baseADS})");
    }

    [Fact]
    public void CalculateEffectiveAccuracyProficiency_ReducesWithSuppression()
    {
        float baseProficiency = 0.8f;
        float noSuppressionProf = SuppressionModel.CalculateEffectiveAccuracyProficiency(baseProficiency, 0f);
        float fullSuppressionProf = SuppressionModel.CalculateEffectiveAccuracyProficiency(baseProficiency, 1f);

        Assert.Equal(baseProficiency, noSuppressionProf);
        Assert.True(fullSuppressionProf < baseProficiency,
            $"Full suppression proficiency ({fullSuppressionProf:F3}) should be less than base ({baseProficiency})");
    }

    [Fact]
    public void CalculateEffectiveAccuracyProficiency_RespectsFloor()
    {
        float baseProficiency = 0.6f;
        float fullSuppressionProf = SuppressionModel.CalculateEffectiveAccuracyProficiency(baseProficiency, 1f);

        float expectedFloor = baseProficiency * SuppressionModel.SuppressionProficiencyFloorFactor;
        Assert.True(fullSuppressionProf >= expectedFloor,
            $"Proficiency ({fullSuppressionProf:F3}) should not drop below floor ({expectedFloor:F3})");
    }

    [Fact]
    public void CalculateReactionDelay_ScalesWithSuppression()
    {
        float noSuppressionDelay = SuppressionModel.CalculateReactionDelay(0f);
        float fullSuppressionDelay = SuppressionModel.CalculateReactionDelay(1f);

        Assert.Equal(0f, noSuppressionDelay);
        Assert.Equal(SuppressionModel.MaxReactionDelayMs, fullSuppressionDelay);
    }

    [Fact]
    public void CalculateEffectiveRecoilControlFactor_ReducesWithSuppression()
    {
        float baseControl = 1.0f;
        float noSuppressionControl = SuppressionModel.CalculateEffectiveRecoilControlFactor(baseControl, 0f);
        float fullSuppressionControl = SuppressionModel.CalculateEffectiveRecoilControlFactor(baseControl, 1f);

        Assert.Equal(baseControl, noSuppressionControl);
        Assert.True(fullSuppressionControl < baseControl,
            $"Full suppression recoil control ({fullSuppressionControl:F3}) should be less than base ({baseControl})");
    }

    #endregion

    #region Suppression Threshold

    [Fact]
    public void ShouldApplySuppression_ReturnsTrue_WhenWithinThreshold()
    {
        Assert.True(SuppressionModel.ShouldApplySuppression(0.1f));
        Assert.True(SuppressionModel.ShouldApplySuppression(0.3f));
        Assert.True(SuppressionModel.ShouldApplySuppression(0.5f));
    }

    [Fact]
    public void ShouldApplySuppression_ReturnsFalse_WhenBeyondThreshold()
    {
        Assert.False(SuppressionModel.ShouldApplySuppression(0.6f));
        Assert.False(SuppressionModel.ShouldApplySuppression(1.0f));
        Assert.False(SuppressionModel.ShouldApplySuppression(2.0f));
    }

    #endregion

    #region Suppression Combination

    [Fact]
    public void CombineSuppression_StacksUpToMax()
    {
        float combined = SuppressionModel.CombineSuppression(0.6f, 0.6f);
        Assert.Equal(SuppressionModel.MaxSuppressionLevel, combined);
    }

    [Fact]
    public void CombineSuppression_AddsSuppression()
    {
        float combined = SuppressionModel.CombineSuppression(0.2f, 0.3f);
        Assert.Equal(0.5f, combined);
    }

    #endregion

    #region Operator Suppression State

    [Fact]
    public void Operator_DefaultSuppressionLevel_IsZero()
    {
        var op = new Operator("Test");
        Assert.Equal(0f, op.SuppressionLevel);
        Assert.False(op.IsSuppressed);
    }

    [Fact]
    public void Operator_ApplySuppression_IncreasesLevel()
    {
        var op = new Operator("Test");
        op.ApplySuppression(0.3f, currentTimeMs: 100);

        Assert.Equal(0.3f, op.SuppressionLevel, 3);
        Assert.True(op.IsSuppressed);
    }

    [Fact]
    public void Operator_ApplySuppression_Stacks()
    {
        var op = new Operator("Test");
        op.ApplySuppression(0.3f, currentTimeMs: 100);
        op.ApplySuppression(0.3f, currentTimeMs: 150);

        Assert.Equal(0.6f, op.SuppressionLevel, 3);
    }

    [Fact]
    public void Operator_ApplySuppression_CapsAtMax()
    {
        var op = new Operator("Test");
        op.ApplySuppression(0.8f, currentTimeMs: 100);
        op.ApplySuppression(0.8f, currentTimeMs: 150);

        Assert.Equal(1.0f, op.SuppressionLevel);
    }

    [Fact]
    public void Operator_ApplySuppression_ReturnsTrueWhenBecameSuppressed()
    {
        var op = new Operator("Test");
        
        bool result = op.ApplySuppression(0.3f, currentTimeMs: 100);
        
        Assert.True(result, "Should return true when operator becomes suppressed");
    }

    [Fact]
    public void Operator_ApplySuppression_ReturnsFalseWhenAlreadySuppressed()
    {
        var op = new Operator("Test");
        op.ApplySuppression(0.3f, currentTimeMs: 100);
        
        bool result = op.ApplySuppression(0.2f, currentTimeMs: 150);
        
        Assert.False(result, "Should return false when operator was already suppressed");
    }

    [Fact]
    public void Operator_UpdateSuppressionDecay_DecaysLevel()
    {
        var op = new Operator("Test");
        op.ApplySuppression(0.8f, currentTimeMs: 100);
        
        op.UpdateSuppressionDecay(deltaMs: 1000, currentTimeMs: 1100);
        
        Assert.True(op.SuppressionLevel < 0.8f, $"Suppression should decay. Level: {op.SuppressionLevel}");
    }

    [Fact]
    public void Operator_UpdateSuppressionDecay_ReturnsTrueWhenEnded()
    {
        var op = new Operator("Test");
        op.ApplySuppression(0.15f, currentTimeMs: 100);
        
        // Decay until suppression ends
        bool ended = false;
        for (int i = 0; i < 50 && !ended; i++)
        {
            ended = op.UpdateSuppressionDecay(deltaMs: 500, currentTimeMs: 100 + (i + 1) * 500);
        }
        
        Assert.True(ended, "UpdateSuppressionDecay should return true when suppression ends");
        Assert.False(op.IsSuppressed);
    }

    [Fact]
    public void Operator_ClearSuppression_ResetsToZero()
    {
        var op = new Operator("Test");
        op.ApplySuppression(0.8f, currentTimeMs: 100);
        
        op.ClearSuppression();
        
        Assert.Equal(0f, op.SuppressionLevel);
        Assert.False(op.IsSuppressed);
    }

    [Fact]
    public void Operator_GetEffectiveAccuracyProficiency_CombinesFlinchAndSuppression()
    {
        var op = new Operator("Test")
        {
            AccuracyProficiency = 0.8f
        };
        
        // Apply both flinch and suppression
        op.ApplyFlinch(0.5f);
        op.ApplySuppression(0.5f, currentTimeMs: 100);
        
        float effective = op.GetEffectiveAccuracyProficiency();
        
        Assert.True(effective < op.AccuracyProficiency,
            $"Effective proficiency ({effective:F3}) should be reduced by both flinch and suppression");
    }

    #endregion

    #region No Stun-Lock Verification

    [Fact]
    public void MaxSuppression_DoesNotPreventActions()
    {
        var op = new Operator("Test");
        op.ApplySuppression(1.0f, currentTimeMs: 100);
        
        // Even at max suppression, proficiency should not be zero
        float effectiveProf = op.GetEffectiveAccuracyProficiency();
        Assert.True(effectiveProf > 0f,
            $"Even at max suppression, effective proficiency ({effectiveProf:F3}) should be > 0");
        
        // ADS time should increase but not be infinite
        float baseADS = 200f;
        float effectiveADS = SuppressionModel.CalculateEffectiveADSTime(baseADS, 1.0f);
        Assert.True(effectiveADS < float.MaxValue && effectiveADS > baseADS,
            $"ADS time ({effectiveADS}) should be bounded");
        
        // Reaction delay should be bounded
        float delay = SuppressionModel.CalculateReactionDelay(1.0f);
        Assert.True(delay <= SuppressionModel.MaxReactionDelayMs,
            $"Reaction delay ({delay}) should be bounded");
    }

    #endregion
}
