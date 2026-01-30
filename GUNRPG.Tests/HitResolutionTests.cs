using GUNRPG.Core.Combat;
using GUNRPG.Core.Weapons;
using Xunit;

namespace GUNRPG.Tests;

public class HitResolutionTests
{
    [Fact]
    public void ResolveShot_PerfectAimNoRecoil_HitsTargetBodyPart()
    {
        // Arrange
        var random = new Random(42);
        float operatorAccuracy = 1.0f; // Perfect accuracy (aim error std dev = 0)
        float weaponVerticalRecoil = 0f; // No recoil
        float currentRecoilY = 0f;
        float recoilVariance = 0f;

        // Act - Aim at lower torso (center: 0.125°)
        // With perfect accuracy and no recoil, shot should be deterministic
        var result = HitResolution.ResolveShot(
            BodyPart.LowerTorso,
            operatorAccuracy,
            weaponVerticalRecoil,
            currentRecoilY,
            recoilVariance,
            random);

        // Assert - With perfect accuracy and no recoil, should hit exactly the target
        // Perfect accuracy means aim error standard deviation is 0, making the shot deterministic
        Assert.Equal(BodyPart.LowerTorso, result.HitLocation);
    }

    [Fact]
    public void ResolveShot_WithVerticalRecoil_HitsHigherBodyPart()
    {
        // Arrange
        var random = new Random(42);
        float operatorAccuracy = 1.0f; // Perfect accuracy
        float weaponVerticalRecoil = 0.3f; // Moderate upward recoil
        float currentRecoilY = 0f;
        float recoilVariance = 0f;

        // Act - Aim at lower torso (center: 0.125°)
        // With 0.3° recoil, should hit upper torso or neck
        var result = HitResolution.ResolveShot(
            BodyPart.LowerTorso,
            operatorAccuracy,
            weaponVerticalRecoil,
            currentRecoilY,
            recoilVariance,
            random);

        // Assert
        Assert.True(result.HitLocation == BodyPart.UpperTorso || 
                    result.HitLocation == BodyPart.Neck,
                    $"Expected UpperTorso or Neck with recoil, got {result.HitLocation}");
        Assert.True(result.FinalAngleDegrees > 0.25f, "Final angle should be higher than lower torso");
    }

    [Fact]
    public void ResolveShot_ExcessiveRecoil_MissesOvershoot()
    {
        // Arrange
        var random = new Random(42);
        float operatorAccuracy = 1.0f; // Perfect accuracy
        float weaponVerticalRecoil = 0.5f; // High recoil
        float currentRecoilY = 0.6f; // Accumulated recoil
        float recoilVariance = 0f;

        // Act - Total recoil > 1.0°, should miss
        var result = HitResolution.ResolveShot(
            BodyPart.LowerTorso,
            operatorAccuracy,
            weaponVerticalRecoil,
            currentRecoilY,
            recoilVariance,
            random);

        // Assert
        Assert.Equal(BodyPart.Miss, result.HitLocation);
        Assert.True(result.FinalAngleDegrees > 1.0f, "Should overshoot the head");
    }

    [Fact]
    public void ResolveShot_NegativeAngle_MissesUndershoot()
    {
        // Arrange
        float operatorAccuracy = 0.0f; // Poor accuracy = high variance
        float weaponVerticalRecoil = -0.5f; // Downward (unusual but testing bounds)
        float currentRecoilY = 0f;
        float recoilVariance = 0f;

        // Act - Multiple attempts to get a miss
        bool foundUndershoot = false;
        for (int i = 0; i < 100; i++)
        {
            var result = HitResolution.ResolveShot(
                BodyPart.LowerTorso,
                operatorAccuracy,
                weaponVerticalRecoil,
                currentRecoilY,
                recoilVariance,
                new Random(i));

            if (result.HitLocation == BodyPart.Miss && result.FinalAngleDegrees < 0f)
            {
                foundUndershoot = true;
                break;
            }
        }

        // Assert
        Assert.True(foundUndershoot, "Should be able to undershoot with poor accuracy and negative recoil");
    }

    [Fact]
    public void ResolveShot_LowerAccuracy_HigherAimError()
    {
        // Arrange
        float weaponVerticalRecoil = 0f;
        float currentRecoilY = 0f;
        float recoilVariance = 0f;

        // Act - Test multiple shots with different accuracy levels
        var resultsHighAccuracy = new List<float>();
        var resultsLowAccuracy = new List<float>();

        for (int i = 0; i < 50; i++)
        {
            var highResult = HitResolution.ResolveShot( BodyPart.LowerTorso, 0.9f, weaponVerticalRecoil, 
                currentRecoilY, recoilVariance, new Random(i));
            resultsHighAccuracy.Add(Math.Abs(highResult.FinalAngleDegrees - 0.125f));

            var lowResult = HitResolution.ResolveShot( BodyPart.LowerTorso, 0.1f, weaponVerticalRecoil, 
                currentRecoilY, recoilVariance, new Random(i));
            resultsLowAccuracy.Add(Math.Abs(lowResult.FinalAngleDegrees - 0.125f));
        }

        // Assert - Low accuracy should have more deviation on average
        float highAccuracyAvgError = resultsHighAccuracy.Average();
        float lowAccuracyAvgError = resultsLowAccuracy.Average();

        Assert.True(lowAccuracyAvgError > highAccuracyAvgError,
            $"Low accuracy error ({lowAccuracyAvgError:F4}) should be > high accuracy error ({highAccuracyAvgError:F4})");
    }

    [Fact]
    public void ResolveShot_WithRecoilVariance_ProducesDifferentResults()
    {
        // Arrange
        float operatorAccuracy = 0.8f;
        float weaponVerticalRecoil = 0.2f;
        float currentRecoilY = 0f;
        float recoilVariance = 0.1f; // 10% variance

        // Act - Multiple shots with different seeds produce variation from both RNG and recoil variance
        var results = new List<float>();
        for (int i = 0; i < 20; i++)
        {
            var result = HitResolution.ResolveShot(
                BodyPart.LowerTorso,
                operatorAccuracy,
                weaponVerticalRecoil,
                currentRecoilY,
                recoilVariance,
                new Random(i));
            results.Add(result.FinalAngleDegrees);
        }

        // Assert - Should have some variation in results
        float stdDev = CalculateStdDev(results);
        Assert.True(stdDev > 0.05f, $"Should have significant variation with variance, got stdDev: {stdDev}");
    }

    [Fact]
    public void ResolveShot_TargetHead_HigherBaseAngle()
    {
        // Arrange
        var random = new Random(42);
        float operatorAccuracy = 1.0f; // Perfect accuracy
        float weaponVerticalRecoil = 0f;
        float currentRecoilY = 0f;
        float recoilVariance = 0f;

        // Act - Aim at head
        var result = HitResolution.ResolveShot(
            BodyPart.Head,
            operatorAccuracy,
            weaponVerticalRecoil,
            currentRecoilY,
            recoilVariance,
            random);

        // Assert - With perfect aim and no recoil, should be near head region
        Assert.True(result.FinalAngleDegrees >= 0.75f, 
            $"Aiming at head should result in angle >= 0.75°, got {result.FinalAngleDegrees}");
    }

    [Fact]
    public void ResolveShot_AngularBands_CorrectMapping()
    {
        // Arrange
        var random = new Random(42);
        float operatorAccuracy = 1.0f;
        float weaponVerticalRecoil = 0f;
        float currentRecoilY = 0f;
        float recoilVariance = 0f;

        // Act & Assert - Test each body part band
        var testCases = new[]
        {
            (BodyPart.LowerTorso, 0.00f, 0.25f),
            (BodyPart.UpperTorso, 0.25f, 0.50f),
            (BodyPart.Neck, 0.50f, 0.75f),
            (BodyPart.Head, 0.75f, 1.00f)
        };

        foreach (var (targetPart, minAngle, maxAngle) in testCases)
        {
            var result = HitResolution.ResolveShot(
                targetPart, operatorAccuracy, weaponVerticalRecoil,
                currentRecoilY, recoilVariance, random);

            // With perfect accuracy and no recoil, should hit within or very close to target band
            Assert.True(result.FinalAngleDegrees >= minAngle - 0.05f && 
                       result.FinalAngleDegrees <= maxAngle + 0.05f,
                $"Target {targetPart} (band {minAngle}-{maxAngle}°) resulted in angle {result.FinalAngleDegrees}°");
        }
    }

    [Fact]
    public void ResolveShot_Deterministic_SameSeedSameResult()
    {
        // Arrange
        float operatorAccuracy = 0.7f;
        float weaponVerticalRecoil = 0.15f;
        float currentRecoilY = 0.05f;
        float recoilVariance = 0.05f;

        // Act - Run twice with same seed
        var result1 = HitResolution.ResolveShot( BodyPart.UpperTorso, operatorAccuracy, weaponVerticalRecoil,
            currentRecoilY, recoilVariance, new Random(999));

        var result2 = HitResolution.ResolveShot( BodyPart.UpperTorso, operatorAccuracy, weaponVerticalRecoil,
            currentRecoilY, recoilVariance, new Random(999));

        // Assert - Should be identical
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
