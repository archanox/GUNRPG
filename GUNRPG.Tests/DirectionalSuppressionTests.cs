using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for directional movement affecting suppression buildup.
/// </summary>
public class DirectionalSuppressionTests
{
    [Theory]
    [InlineData(MovementDirection.Holding, 1.0f)]
    [InlineData(MovementDirection.Advancing, 1.2f)]
    [InlineData(MovementDirection.Retreating, 0.85f)]
    public void GetDirectionalSuppressionMultiplier_ReturnsCorrectValues(MovementDirection direction, float expected)
    {
        float actual = MovementModel.GetDirectionalSuppressionMultiplier(direction);
        Assert.Equal(expected, actual, precision: 2);
    }

    [Theory]
    [InlineData(MovementDirection.Holding, 1.0f)]
    [InlineData(MovementDirection.Advancing, 1.15f)]
    [InlineData(MovementDirection.Retreating, 0.9f)]
    public void GetDirectionalHitProbabilityMultiplier_ReturnsCorrectValues(MovementDirection direction, float expected)
    {
        float actual = MovementModel.GetDirectionalHitProbabilityMultiplier(direction);
        Assert.Equal(expected, actual, precision: 2);
    }

    [Fact]
    public void AdvancingDirection_IncreasesSuppression()
    {
        // Arrange
        var weapon = WeaponFactory.CreateM15Mod0();

        // Act
        float suppressionAdvancing = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary,
            targetDirection: MovementDirection.Advancing);

        float suppressionHolding = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary,
            targetDirection: MovementDirection.Holding);

        // Assert
        Assert.True(suppressionAdvancing > suppressionHolding,
            $"Advancing should increase suppression. Advancing={suppressionAdvancing}, Holding={suppressionHolding}");
    }

    [Fact]
    public void RetreatingDirection_ReducesSuppression()
    {
        // Arrange
        var weapon = WeaponFactory.CreateM15Mod0();

        // Act
        float suppressionRetreating = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary,
            targetDirection: MovementDirection.Retreating);

        float suppressionHolding = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary,
            targetDirection: MovementDirection.Holding);

        // Assert
        Assert.True(suppressionRetreating < suppressionHolding,
            $"Retreating should reduce suppression. Retreating={suppressionRetreating}, Holding={suppressionHolding}");
    }

    [Fact]
    public void DirectionalModifier_StacksWithMovementState()
    {
        // Arrange
        var weapon = WeaponFactory.CreateM15Mod0();

        // Act - Advancing + Sprinting (both increase suppression)
        float suppressionAdvancingSprinting = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Sprinting,
            targetDirection: MovementDirection.Advancing);

        // Baseline - Holding + Stationary
        float suppressionBaseline = SuppressionModel.CalculateSuppressionSeverity(
            weapon.SuppressionFactor,
            weapon.RoundsPerMinute,
            distanceMeters: 15f,
            angularDeviationDegrees: 0.3f,
            targetMovementState: MovementState.Stationary,
            targetDirection: MovementDirection.Holding);

        // Assert - Both modifiers should stack
        Assert.True(suppressionAdvancingSprinting > suppressionBaseline,
            $"Advancing + Sprinting should significantly increase suppression. Combined={suppressionAdvancingSprinting}, Baseline={suppressionBaseline}");

        // The combined effect should be multiplicative
        float expectedMultiplier = MovementModel.GetSuppressionBuildupMultiplier(MovementState.Sprinting) *
                                   MovementModel.GetDirectionalSuppressionMultiplier(MovementDirection.Advancing);
        
        float actualMultiplier = suppressionAdvancingSprinting / suppressionBaseline;
        
        Assert.Equal(expectedMultiplier, actualMultiplier, precision: 1);
    }

    [Fact]
    public void OperatorDefaultDirection_IsHolding()
    {
        // Arrange & Act
        var op = new Operator("Test");

        // Assert
        Assert.Equal(MovementDirection.Holding, op.CurrentDirection);
    }

    [Fact]
    public void MovementDirection_DoesNotAffectDistance()
    {
        // This is an architectural test - ensure direction doesn't have distance side effects
        // Arrange
        var op = new Operator("Test")
        {
            DistanceToOpponent = 15f,
            CurrentDirection = MovementDirection.Holding
        };

        float initialDistance = op.DistanceToOpponent;

        // Act - Change direction
        op.CurrentDirection = MovementDirection.Advancing;

        // Assert - Distance should remain unchanged
        Assert.Equal(initialDistance, op.DistanceToOpponent);

        // Act - Change direction again
        op.CurrentDirection = MovementDirection.Retreating;

        // Assert - Distance still unchanged
        Assert.Equal(initialDistance, op.DistanceToOpponent);
    }
}
