using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for the tactical posture system.
/// Tactical posture affects suppression and hit probability, but NOT spatial distance.
/// </summary>
public class TacticalPostureTests
{
    [Theory]
    [InlineData(TacticalPosture.Hold, 1.0f)]
    [InlineData(TacticalPosture.Advance, 1.2f)]
    [InlineData(TacticalPosture.Retreat, 0.85f)]
    public void GetTacticalPostureSuppressionMultiplier_ReturnsCorrectValues(TacticalPosture posture, float expected)
    {
        float actual = MovementModel.GetTacticalPostureSuppressionMultiplier(posture);
        Assert.Equal(expected, actual, precision: 2);
    }

    [Theory]
    [InlineData(TacticalPosture.Hold, 1.0f)]
    [InlineData(TacticalPosture.Advance, 1.15f)]
    [InlineData(TacticalPosture.Retreat, 0.9f)]
    public void GetTacticalPostureHitProbabilityMultiplier_ReturnsCorrectValues(TacticalPosture posture, float expected)
    {
        float actual = MovementModel.GetTacticalPostureHitProbabilityMultiplier(posture);
        Assert.Equal(expected, actual, precision: 2);
    }

    [Fact]
    public void AdvancePosture_IncreasesSuppression()
    {
        // Arrange
        var weapon = WeaponFactory.CreateM15Mod0();

        // Act - Use angular deviation well within threshold (0.3 < 0.5)
        float suppressionAdvance = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary,
            targetPosture: TacticalPosture.Advance);

        float suppressionHold = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary,
            targetPosture: TacticalPosture.Hold);

        // Assert
        Assert.True(suppressionAdvance > suppressionHold,
            $"Advance posture should increase suppression. Advance={suppressionAdvance}, Hold={suppressionHold}");
    }

    [Fact]
    public void RetreatPosture_ReducesSuppression()
    {
        // Arrange
        var weapon = WeaponFactory.CreateM15Mod0();

        // Act - Use angular deviation well within threshold (0.3 < 0.5)
        float suppressionRetreat = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary,
            targetPosture: TacticalPosture.Retreat);

        float suppressionHold = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary,
            targetPosture: TacticalPosture.Hold);

        // Assert
        Assert.True(suppressionRetreat < suppressionHold,
            $"Retreat posture should reduce suppression. Retreat={suppressionRetreat}, Hold={suppressionHold}");
    }

    [Fact]
    public void TacticalPosture_StacksWithMovementState()
    {
        // Arrange
        var weapon = WeaponFactory.CreateM15Mod0();

        // Act - Advance + Sprinting (both increase suppression)
        // Use angular deviation well within threshold (0.3 < 0.5)
        float suppressionAdvanceSprinting = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Sprinting,
            targetPosture: TacticalPosture.Advance);

        // Baseline - Hold + Stationary
        float suppressionBaseline = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary,
            targetPosture: TacticalPosture.Hold);

        // Assert - Both modifiers should stack
        Assert.True(suppressionAdvanceSprinting > suppressionBaseline,
            $"Advance + Sprinting should significantly increase suppression. Combined={suppressionAdvanceSprinting}, Baseline={suppressionBaseline}");

        // The combined effect should be multiplicative
        float expectedMultiplier = MovementModel.GetSuppressionBuildupMultiplier(MovementState.Sprinting) *
                                   MovementModel.GetTacticalPostureSuppressionMultiplier(TacticalPosture.Advance);
        
        float actualMultiplier = suppressionAdvanceSprinting / suppressionBaseline;
        
        Assert.Equal(expectedMultiplier, actualMultiplier, precision: 1);
    }

    [Fact]
    public void OperatorDefaultPosture_IsHold()
    {
        // Arrange & Act
        var op = new Operator("Test");

        // Assert
        Assert.Equal(TacticalPosture.Hold, op.CurrentPosture);
    }

    [Fact]
    public void TacticalPosture_DoesNotAffectDistance()
    {
        // This is an architectural test - ensure posture doesn't have distance side effects
        // Arrange
        var op = new Operator("Test")
        {
            DistanceToOpponent = 15f,
            CurrentPosture = TacticalPosture.Hold
        };

        float initialDistance = op.DistanceToOpponent;

        // Act - Change posture
        op.CurrentPosture = TacticalPosture.Advance;

        // Assert - Distance should remain unchanged
        Assert.Equal(initialDistance, op.DistanceToOpponent);

        // Act - Change posture again
        op.CurrentPosture = TacticalPosture.Retreat;

        // Assert - Distance still unchanged
        Assert.Equal(initialDistance, op.DistanceToOpponent);
    }
}
