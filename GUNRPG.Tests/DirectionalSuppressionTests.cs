using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for directional movement affecting suppression buildup and hit probability.
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

    [Fact]
    public void DirectionalHitProbability_IntegratedIntoHitResolution()
    {
        // This test verifies that directional movement affects hit probability
        // by comparing overall hit rates with different directions
        
        // Arrange
        var weapon = WeaponFactory.CreateM15Mod0();
        
        // Act - Resolve multiple shots and count ANY hit (not just head)
        int advancingHits = 0;
        int holdingHits = 0;
        int retreatingHits = 0;
        int iterations = 500;
        
        for (int i = 0; i < iterations; i++)
        {
            // Use lower accuracy to make the directional modifier more visible
            float accuracy = 0.55f;
            float proficiency = 0.5f;
            float recoil = weapon.VerticalRecoil * 0.4f;
            
            // Test advancing target (easier to hit) - 15% bonus = 1.15x multiplier
            var randomAdv = new Random(42 + i);
            var resultAdvancing = HitResolution.ResolveShotWithProficiency(
                BodyPart.UpperTorso, accuracy, proficiency, weapon.VerticalRecoil, recoil, 0.08f,
                randomAdv, targetDirection: MovementDirection.Advancing);
            if (resultAdvancing.HitLocation != BodyPart.Miss) advancingHits++;
            
            // Test holding target (baseline) - 1.0x multiplier
            var randomHold = new Random(42 + i);
            var resultHolding = HitResolution.ResolveShotWithProficiency(
                BodyPart.UpperTorso, accuracy, proficiency, weapon.VerticalRecoil, recoil, 0.08f,
                randomHold, targetDirection: MovementDirection.Holding);
            if (resultHolding.HitLocation != BodyPart.Miss) holdingHits++;
            
            // Test retreating target (harder to hit) - 10% penalty = 0.9x multiplier
            var randomRetr = new Random(42 + i);
            var resultRetreating = HitResolution.ResolveShotWithProficiency(
                BodyPart.UpperTorso, accuracy, proficiency, weapon.VerticalRecoil, recoil, 0.08f,
                randomRetr, targetDirection: MovementDirection.Retreating);
            if (resultRetreating.HitLocation != BodyPart.Miss) retreatingHits++;
        }
        
        float advancingRate = advancingHits / (float)iterations;
        float holdingRate = holdingHits / (float)iterations;
        float retreatingRate = retreatingHits / (float)iterations;
        
        // Assert - The directional modifier should create visible differences in hit rates
        // Advancing (1.15x) should have higher hit rate than holding (1.0x)
        // Holding (1.0x) should have higher hit rate than retreating (0.9x)
        Assert.True(advancingHits >= holdingHits,
            $"Advancing targets should be at least as easy to hit as holding. Advancing={advancingHits} ({advancingRate:P}), Holding={holdingHits} ({holdingRate:P})");
        Assert.True(holdingHits >= retreatingHits,
            $"Holding targets should be at least as easy to hit as retreating. Holding={holdingHits} ({holdingRate:P}), Retreating={retreatingHits} ({retreatingRate:P})");
        
        // At least verify that the integration is working (not all zeros or all same)
        int totalHits = advancingHits + holdingHits + retreatingHits;
        Assert.True(totalHits > 0, "At least some shots should hit to validate the test");
        Assert.True(advancingHits != holdingHits || holdingHits != retreatingHits,
            $"Directional modifiers should create some variation. Advancing={advancingHits}, Holding={holdingHits}, Retreating={retreatingHits}");
    }
}
