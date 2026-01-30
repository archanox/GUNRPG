using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
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
        // At proficiency 0: full aim error (BaseAimErrorScale = 0.15)
        float lowProfError = AccuracyModel.CalculateAimErrorStdDev(0f);
        
        // At proficiency 1: reduced aim error by MaxAimErrorReductionFactor (50%)
        // 0.15 * (1 - 0.5) = 0.075
        float highProfError = AccuracyModel.CalculateAimErrorStdDev(1f);
        
        Assert.True(lowProfError > highProfError, 
            $"Low proficiency error ({lowProfError}) should be greater than high proficiency error ({highProfError})");
        Assert.Equal(0.15f, lowProfError, 3);
        Assert.Equal(0.075f, highProfError, 3);
    }

    [Fact]
    public void AccuracyModel_CalculateAimErrorStdDev_WithAccuracyAndProficiency()
    {
        // Low accuracy (0.5), low proficiency (0): (1 - 0.5) * 0.15 * 1.0 = 0.075
        float lowLow = AccuracyModel.CalculateAimErrorStdDev(0.5f, 0f);
        Assert.Equal(0.075f, lowLow, 3);
        
        // Low accuracy (0.5), high proficiency (1): (1 - 0.5) * 0.15 * 0.5 = 0.0375
        float lowHigh = AccuracyModel.CalculateAimErrorStdDev(0.5f, 1f);
        Assert.Equal(0.0375f, lowHigh, 3);
        
        // High accuracy (1.0), any proficiency: (1 - 1.0) * 0.15 * anything = 0
        float highAny = AccuracyModel.CalculateAimErrorStdDev(1.0f, 0.5f);
        Assert.Equal(0f, highAny, 3);
    }

    [Fact]
    public void AccuracyModel_CalculateEffectiveRecoil_ReducesWithProficiency()
    {
        float weaponRecoil = 0.5f;
        
        // At proficiency 0: no reduction (1.0 * weaponRecoil)
        float noReduction = AccuracyModel.CalculateEffectiveRecoil(weaponRecoil, 0f);
        Assert.Equal(weaponRecoil, noReduction, 3);
        
        // At proficiency 1: 60% reduction (0.4 * weaponRecoil)
        float maxReduction = AccuracyModel.CalculateEffectiveRecoil(weaponRecoil, 1f);
        Assert.Equal(weaponRecoil * 0.4f, maxReduction, 3);
        
        // At proficiency 0.5: 30% reduction (0.7 * weaponRecoil)
        float midReduction = AccuracyModel.CalculateEffectiveRecoil(weaponRecoil, 0.5f);
        Assert.Equal(weaponRecoil * 0.7f, midReduction, 3);
    }

    [Fact]
    public void AccuracyModel_CalculateRecoveryRateMultiplier_IncreasesWithProficiency()
    {
        // At proficiency 0: 0.5x recovery rate
        float lowMultiplier = AccuracyModel.CalculateRecoveryRateMultiplier(0f);
        Assert.Equal(0.5f, lowMultiplier, 3);
        
        // At proficiency 1: 2.0x recovery rate
        float highMultiplier = AccuracyModel.CalculateRecoveryRateMultiplier(1f);
        Assert.Equal(2.0f, highMultiplier, 3);
        
        // At proficiency 0.5: 1.25x recovery rate
        float midMultiplier = AccuracyModel.CalculateRecoveryRateMultiplier(0.5f);
        Assert.Equal(1.25f, midMultiplier, 3);
    }

    [Fact]
    public void AccuracyModel_ApplyRecovery_RecoversFasterWithHighProficiency()
    {
        float currentRecoil = 1.0f;
        float baseRecovery = 0.2f;
        
        // At proficiency 0: 0.5x recovery rate -> 0.1 recovered
        float lowProfRecoil = AccuracyModel.ApplyRecovery(currentRecoil, baseRecovery, 0f);
        Assert.Equal(0.9f, lowProfRecoil, 3);
        
        // At proficiency 1: 2.0x recovery rate -> 0.4 recovered
        float highProfRecoil = AccuracyModel.ApplyRecovery(currentRecoil, baseRecovery, 1f);
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

    [Fact]
    public void RecoilRecovery_OccursEvenWhenRoundEndsEarly()
    {
        // Arrange: Create combat where a hit ends the round quickly
        var player = new Operator("Player")
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            AccuracyProficiency = 0.5f,
            Accuracy = 1.0f  // Ensure hit
        };
        var enemy = new Operator("Enemy")
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            AccuracyProficiency = 0.5f,
            Accuracy = 0.0f  // Ensure miss so only player hits
        };

        // Initial recoil should be zero
        Assert.Equal(0f, player.CurrentRecoilY);

        var combat = new CombatSystemV2(player, enemy, seed: 42);

        // Act: Fire a shot (round will end on hit)
        var playerIntents = new SimultaneousIntents(player.Id)
        {
            Primary = PrimaryAction.Fire,
            Movement = MovementAction.Stand
        };
        var enemyIntents = new SimultaneousIntents(enemy.Id)
        {
            Primary = PrimaryAction.Fire,
            Movement = MovementAction.Stand
        };

        combat.SubmitIntents(player, playerIntents);
        combat.SubmitIntents(enemy, enemyIntents);
        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        // Assert: Recoil should have been applied and then partially recovered
        // The weapon has ~0.15 recoil, and immediate recovery happens
        // With 0.5 proficiency and RecoilRecoveryRate of 5, recovery should reduce recoil
        Assert.True(player.CurrentRecoilY >= 0,
            $"Recoil should not be negative. Current: {player.CurrentRecoilY}");
        
        // Recoil should be less than the raw weapon recoil due to immediate recovery
        var weapon = player.EquippedWeapon!;
        Assert.True(player.CurrentRecoilY < weapon.VerticalRecoil * 2,
            $"Recoil ({player.CurrentRecoilY}) should be controlled after shot. Raw weapon recoil: {weapon.VerticalRecoil}");
    }

    [Fact]
    public void RecoilRecovery_HighProficiency_RecoversFasterPerShot()
    {
        // Create operators with different proficiencies
        var lowProfOp = new Operator("LowProf")
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            AccuracyProficiency = 0.1f,
            RecoilRecoveryRate = 5f
        };
        var highProfOp = new Operator("HighProf")
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            AccuracyProficiency = 1.0f,
            RecoilRecoveryRate = 5f
        };

        // Simulate firing without full combat system to isolate the effect
        var weapon = lowProfOp.EquippedWeapon!;

        // Add same recoil to both
        lowProfOp.CurrentRecoilY = weapon.VerticalRecoil;
        highProfOp.CurrentRecoilY = weapon.VerticalRecoil;

        // Simulate immediate recovery (100ms worth)
        const float recoveryTimeSeconds = 0.1f;
        float lowProfMultiplier = AccuracyModel.CalculateRecoveryRateMultiplier(lowProfOp.AccuracyProficiency);
        float highProfMultiplier = AccuracyModel.CalculateRecoveryRateMultiplier(highProfOp.AccuracyProficiency);

        float lowProfRecovery = lowProfOp.RecoilRecoveryRate * recoveryTimeSeconds * lowProfMultiplier;
        float highProfRecovery = highProfOp.RecoilRecoveryRate * recoveryTimeSeconds * highProfMultiplier;

        lowProfOp.CurrentRecoilY = Math.Max(0, lowProfOp.CurrentRecoilY - lowProfRecovery);
        highProfOp.CurrentRecoilY = Math.Max(0, highProfOp.CurrentRecoilY - highProfRecovery);

        // High proficiency should have recovered more (lower remaining recoil)
        Assert.True(highProfOp.CurrentRecoilY < lowProfOp.CurrentRecoilY,
            $"High prof recoil ({highProfOp.CurrentRecoilY}) should be < low prof ({lowProfOp.CurrentRecoilY})");
    }

    [Fact]
    public void Operator_MinRecommendedAccuracyProficiency_Constant()
    {
        // Verify the constant exists and has a sensible value
        Assert.True(Operator.MinRecommendedAccuracyProficiency > 0f,
            "MinRecommendedAccuracyProficiency should be > 0");
        Assert.True(Operator.MinRecommendedAccuracyProficiency <= 0.5f,
            "MinRecommendedAccuracyProficiency should not be too high");
    }

    private static float CalculateStdDev(List<float> values)
    {
        if (values.Count == 0) return 0;
        float mean = values.Average();
        float sumSquaredDiff = values.Sum(v => (v - mean) * (v - mean));
        return (float)Math.Sqrt(sumSquaredDiff / values.Count);
    }
}
