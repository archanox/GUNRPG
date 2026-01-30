using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for the Operator Accuracy Proficiency system.
/// Validates that proficiency affects recoil control and aim stability
/// without modifying weapon base stats.
/// </summary>
public class AccuracyProficiencyTests
{
    [Fact]
    public void Operator_DefaultAccuracyProficiency_IsMidRange()
    {
        var op = new Operator("Test");
        
        // Default proficiency should be 0.5 (mid-range)
        Assert.Equal(0.5f, op.AccuracyProficiency);
    }

    [Fact]
    public void AccuracyProficiency_ClampedToValidRange()
    {
        var op = new Operator("Test");
        
        op.AccuracyProficiency = 1.5f;
        Assert.Equal(1.0f, op.AccuracyProficiency);
        
        op.AccuracyProficiency = -0.5f;
        Assert.Equal(0.0f, op.AccuracyProficiency);
    }

    [Fact]
    public void AccuracyModel_CalculateAimErrorStdDev_ScalesWithProficiency()
    {
        var model = new AccuracyModel(new Random(42));
        
        // At proficiency 0: full aim error (0.15)
        float lowProfError = model.CalculateAimErrorStdDev(0f);
        
        // At proficiency 1: reduced aim error (0.01 minimum)
        float highProfError = model.CalculateAimErrorStdDev(1f);
        
        Assert.True(lowProfError > highProfError, 
            $"Low proficiency error ({lowProfError}) should be greater than high proficiency error ({highProfError})");
        Assert.Equal(0.15f, lowProfError, 3);
        Assert.Equal(0.01f, highProfError, 3);
    }

    [Fact]
    public void AccuracyModel_CalculateEffectiveRecoil_ReducesWithProficiency()
    {
        var model = new AccuracyModel(new Random(42));
        float weaponRecoil = 0.5f;
        
        // At proficiency 0: no reduction (1.0 * weaponRecoil)
        float noReduction = model.CalculateEffectiveRecoil(weaponRecoil, 0f);
        Assert.Equal(weaponRecoil, noReduction, 3);
        
        // At proficiency 1: 60% reduction (0.4 * weaponRecoil)
        float maxReduction = model.CalculateEffectiveRecoil(weaponRecoil, 1f);
        Assert.Equal(weaponRecoil * 0.4f, maxReduction, 3);
        
        // At proficiency 0.5: 30% reduction (0.7 * weaponRecoil)
        float midReduction = model.CalculateEffectiveRecoil(weaponRecoil, 0.5f);
        Assert.Equal(weaponRecoil * 0.7f, midReduction, 3);
    }

    [Fact]
    public void AccuracyModel_CalculateRecoveryRateMultiplier_IncreasesWithProficiency()
    {
        var model = new AccuracyModel(new Random(42));
        
        // At proficiency 0: 0.5x recovery rate
        float lowMultiplier = model.CalculateRecoveryRateMultiplier(0f);
        Assert.Equal(0.5f, lowMultiplier, 3);
        
        // At proficiency 1: 2.0x recovery rate
        float highMultiplier = model.CalculateRecoveryRateMultiplier(1f);
        Assert.Equal(2.0f, highMultiplier, 3);
        
        // At proficiency 0.5: 1.25x recovery rate
        float midMultiplier = model.CalculateRecoveryRateMultiplier(0.5f);
        Assert.Equal(1.25f, midMultiplier, 3);
    }

    [Fact]
    public void AccuracyModel_ApplyRecovery_RecoversFasterWithHighProficiency()
    {
        var model = new AccuracyModel(new Random(42));
        float currentRecoil = 1.0f;
        float baseRecovery = 0.2f;
        
        // At proficiency 0: 0.5x recovery rate -> 0.1 recovered
        float lowProfRecoil = model.ApplyRecovery(currentRecoil, baseRecovery, 0f);
        Assert.Equal(0.9f, lowProfRecoil, 3);
        
        // At proficiency 1: 2.0x recovery rate -> 0.4 recovered
        float highProfRecoil = model.ApplyRecovery(currentRecoil, baseRecovery, 1f);
        Assert.Equal(0.6f, highProfRecoil, 3);
    }

    [Fact]
    public void HitResolution_ResolveShotWithProficiency_ReducesEffectiveRecoil()
    {
        // Test that high proficiency reduces effective vertical recoil
        float weaponRecoil = 0.5f;
        float currentRecoil = 0.2f;
        
        var lowProfResults = new List<float>();
        var highProfResults = new List<float>();
        
        for (int i = 0; i < 50; i++)
        {
            // Low proficiency shot
            var lowResult = HitResolution.ResolveShotWithProficiency(
                BodyPart.UpperTorso,
                1.0f, // Perfect accuracy (no aim error)
                0.0f, // Low proficiency
                weaponRecoil,
                currentRecoil,
                0f, // No variance
                new Random(i));
            lowProfResults.Add(lowResult.FinalAngleDegrees);
            
            // High proficiency shot
            var highResult = HitResolution.ResolveShotWithProficiency(
                BodyPart.UpperTorso,
                1.0f, // Perfect accuracy (no aim error)
                1.0f, // High proficiency
                weaponRecoil,
                currentRecoil,
                0f, // No variance
                new Random(i));
            highProfResults.Add(highResult.FinalAngleDegrees);
        }
        
        float avgLowProf = lowProfResults.Average();
        float avgHighProf = highProfResults.Average();
        
        // High proficiency should result in lower final angles (less recoil effect)
        Assert.True(avgHighProf < avgLowProf,
            $"High proficiency average ({avgHighProf:F4}) should be lower than low proficiency ({avgLowProf:F4})");
    }

    [Fact]
    public void HitResolution_ResolveShotWithProficiency_TightensAimDistribution()
    {
        // Test that high proficiency tightens aim error distribution
        var lowProfResults = new List<float>();
        var highProfResults = new List<float>();
        
        for (int i = 0; i < 100; i++)
        {
            // Low proficiency shot (with medium accuracy to see aim error effects)
            var lowResult = HitResolution.ResolveShotWithProficiency(
                BodyPart.LowerTorso,
                0.5f, // Medium accuracy
                0.0f, // Low proficiency
                0f, // No weapon recoil
                0f, // No accumulated recoil
                0f, // No variance
                new Random(i));
            lowProfResults.Add(lowResult.FinalAngleDegrees);
            
            // High proficiency shot
            var highResult = HitResolution.ResolveShotWithProficiency(
                BodyPart.LowerTorso,
                0.5f, // Medium accuracy
                1.0f, // High proficiency
                0f, // No weapon recoil
                0f, // No accumulated recoil
                0f, // No variance
                new Random(i));
            highProfResults.Add(highResult.FinalAngleDegrees);
        }
        
        // Calculate standard deviation of results
        float lowProfStdDev = CalculateStdDev(lowProfResults);
        float highProfStdDev = CalculateStdDev(highProfResults);
        
        // High proficiency should have tighter distribution (lower std dev)
        Assert.True(highProfStdDev < lowProfStdDev,
            $"High proficiency std dev ({highProfStdDev:F4}) should be lower than low proficiency ({lowProfStdDev:F4})");
    }

    [Fact]
    public void HitResolution_ResolveShotWithProficiency_DoesNotAffectBodyPartBands()
    {
        // Test that body part bands remain unchanged regardless of proficiency
        var random = new Random(42);
        
        var testCases = new[]
        {
            (BodyPart.LowerTorso, 0.00f, 0.25f),
            (BodyPart.UpperTorso, 0.25f, 0.50f),
            (BodyPart.Neck, 0.50f, 0.75f),
            (BodyPart.Head, 0.75f, 1.00f)
        };
        
        foreach (var (targetPart, minAngle, maxAngle) in testCases)
        {
            // Perfect accuracy and proficiency, no recoil - should hit target band
            var result = HitResolution.ResolveShotWithProficiency(
                targetPart,
                1.0f, // Perfect accuracy
                1.0f, // Perfect proficiency
                0f, // No recoil
                0f, // No accumulated recoil
                0f, // No variance
                random);
            
            Assert.True(result.FinalAngleDegrees >= minAngle - 0.05f &&
                       result.FinalAngleDegrees <= maxAngle + 0.05f,
                $"Target {targetPart} (band {minAngle}-{maxAngle}°) resulted in angle {result.FinalAngleDegrees}°");
        }
    }

    [Fact]
    public void HitResolution_WeaponRecoilValuesRemainUnchanged()
    {
        // Verify that weapon base stats are not modified by proficiency
        var weapon = WeaponFactory.CreateSturmwolf45();
        float originalRecoil = weapon.VerticalRecoil;
        
        // Create operators with different proficiencies
        var lowProfOp = new Operator("LowProf") { AccuracyProficiency = 0f };
        var highProfOp = new Operator("HighProf") { AccuracyProficiency = 1f };
        
        // Fire shots (simulated)
        var random = new Random(42);
        HitResolution.ResolveShotWithProficiency(
            BodyPart.UpperTorso, lowProfOp.Accuracy, lowProfOp.AccuracyProficiency,
            weapon.VerticalRecoil, 0f, 0f, random);
        
        HitResolution.ResolveShotWithProficiency(
            BodyPart.UpperTorso, highProfOp.Accuracy, highProfOp.AccuracyProficiency,
            weapon.VerticalRecoil, 0f, 0f, random);
        
        // Weapon recoil should remain unchanged
        Assert.Equal(originalRecoil, weapon.VerticalRecoil);
    }

    [Fact]
    public void Operator_RecoilRecovery_AffectedByProficiency()
    {
        // Test that recoil recovery is affected by AccuracyProficiency
        var lowProfOp = new Operator("LowProf")
        {
            AccuracyProficiency = 0f,
            CurrentRecoilY = 1.0f,
            RecoilRecoveryStartMs = 0,
            RecoilRecoveryRate = 1.0f
        };
        
        var highProfOp = new Operator("HighProf")
        {
            AccuracyProficiency = 1f,
            CurrentRecoilY = 1.0f,
            RecoilRecoveryStartMs = 0,
            RecoilRecoveryRate = 1.0f
        };
        
        // Update regeneration for 1 second
        lowProfOp.UpdateRegeneration(1000, 1000);
        highProfOp.UpdateRegeneration(1000, 1000);
        
        // Low proficiency: 0.5x recovery (1.0 * 1.0 * 0.5 = 0.5 recovered -> 0.5 remaining)
        Assert.True(lowProfOp.CurrentRecoilY > highProfOp.CurrentRecoilY,
            $"Low prof recoil ({lowProfOp.CurrentRecoilY:F3}) should be > high prof ({highProfOp.CurrentRecoilY:F3})");
        
        // High proficiency: 2.0x recovery (1.0 * 1.0 * 2.0 = 2.0 recovered -> 0 remaining)
        Assert.Equal(0f, highProfOp.CurrentRecoilY, 3);
    }

    [Fact]
    public void AccuracyProficiency_DoesNotAffectDamage()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();
        
        // Damage should be the same regardless of proficiency
        float damageAtDistance = weapon.GetDamageAtDistance(15f, BodyPart.UpperTorso);
        
        var lowProfOp = new Operator("LowProf") { AccuracyProficiency = 0f, EquippedWeapon = weapon };
        var highProfOp = new Operator("HighProf") { AccuracyProficiency = 1f, EquippedWeapon = weapon };
        
        // Damage retrieval should be unaffected by operator proficiency
        Assert.Equal(damageAtDistance, lowProfOp.EquippedWeapon.GetDamageAtDistance(15f, BodyPart.UpperTorso));
        Assert.Equal(damageAtDistance, highProfOp.EquippedWeapon.GetDamageAtDistance(15f, BodyPart.UpperTorso));
    }

    [Fact]
    public void AccuracyProficiency_DoesNotAffectFireRate()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();
        float originalFireRate = weapon.RoundsPerMinute;
        float originalTimeBetweenShots = weapon.GetTimeBetweenShotsMs();
        
        var lowProfOp = new Operator("LowProf") { AccuracyProficiency = 0f, EquippedWeapon = weapon };
        var highProfOp = new Operator("HighProf") { AccuracyProficiency = 1f, EquippedWeapon = weapon };
        
        // Fire rate should be unaffected by operator proficiency
        Assert.Equal(originalFireRate, lowProfOp.EquippedWeapon.RoundsPerMinute);
        Assert.Equal(originalFireRate, highProfOp.EquippedWeapon.RoundsPerMinute);
        Assert.Equal(originalTimeBetweenShots, lowProfOp.EquippedWeapon.GetTimeBetweenShotsMs());
        Assert.Equal(originalTimeBetweenShots, highProfOp.EquippedWeapon.GetTimeBetweenShotsMs());
    }

    [Fact]
    public void AccuracyModel_SampleAimError_DeterministicWithSeed()
    {
        // Verify deterministic behavior with same seed
        var model1 = new AccuracyModel(new Random(42));
        var model2 = new AccuracyModel(new Random(42));
        
        float error1 = model1.SampleAimError(0.5f);
        float error2 = model2.SampleAimError(0.5f);
        
        Assert.Equal(error1, error2);
    }

    [Fact]
    public void ResolveShotWithProficiency_Deterministic_SameSeedSameResult()
    {
        float operatorAccuracy = 0.7f;
        float proficiency = 0.5f;
        float weaponVerticalRecoil = 0.15f;
        float currentRecoilY = 0.05f;
        float recoilVariance = 0.05f;

        var result1 = HitResolution.ResolveShotWithProficiency(
            BodyPart.UpperTorso, operatorAccuracy, proficiency, weaponVerticalRecoil,
            currentRecoilY, recoilVariance, new Random(999));

        var result2 = HitResolution.ResolveShotWithProficiency(
            BodyPart.UpperTorso, operatorAccuracy, proficiency, weaponVerticalRecoil,
            currentRecoilY, recoilVariance, new Random(999));

        Assert.Equal(result1.HitLocation, result2.HitLocation);
        Assert.Equal(result1.FinalAngleDegrees, result2.FinalAngleDegrees);
    }

    private static float CalculateStdDev(List<float> values)
    {
        if (values.Count == 0) return 0;
        float mean = values.Average();
        float sumSquaredDiff = values.Sum(v => (v - mean) * (v - mean));
        return (float)Math.Sqrt(sumSquaredDiff / values.Count);
    }
}
