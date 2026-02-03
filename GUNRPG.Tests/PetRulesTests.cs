using GUNRPG.Core.VirtualPet;
using Xunit;

namespace GUNRPG.Tests;

public class PetRulesTests
{
    private static PetState CreateDefaultState(DateTimeOffset lastUpdated)
    {
        return new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 50f,
            Injury: 0f,
            Stress: 50f,
            Morale: 50f,
            Hunger: 50f,
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
    }

    [Fact]
    public void Apply_UpdatesLastUpdatedToNow()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = CreateDefaultState(lastUpdated);
        var input = new RestInput(TimeSpan.FromHours(1));

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert
        Assert.Equal(now, result.LastUpdated);
    }

    [Fact]
    public void Apply_WithRestInput_RecoversFatigueAndStress()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 60f,
            Injury: 0f,
            Stress: 40f,
            Morale: 50f,
            Hunger: 30f,
            Hydration: 70f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.FromHours(1));

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert
        // Health should increase: 50 + (15 * 1) = 65
        Assert.Equal(65f, result.Health);
        // Fatigue should decrease: 60 - (20 * 1) = 40
        Assert.Equal(40f, result.Fatigue);
        // Stress should decrease: 40 - (12 * 1) = 28
        Assert.Equal(28f, result.Stress);
    }

    [Fact]
    public void Apply_WithEatInput_ReducesHunger()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated;
        var state = CreateDefaultState(lastUpdated) with { Hunger = 60f };
        var input = new EatInput(Nutrition: 30f);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert
        // Hunger should decrease: 60 - 30 = 30
        Assert.Equal(30f, result.Hunger);
    }

    [Fact]
    public void Apply_WithDrinkInput_IncreasesHydration()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated;
        var state = CreateDefaultState(lastUpdated) with { Hydration = 40f };
        var input = new DrinkInput(Hydration: 25f);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert
        // Hydration should increase: 40 + 25 = 65
        Assert.Equal(65f, result.Hydration);
    }

    [Fact]
    public void Apply_WithMissionInput_IncreasesStressAndInjury()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated;
        var state = CreateDefaultState(lastUpdated) with { Stress = 30f, Injury = 5f };
        var input = new MissionInput(StressLoad: 20f, InjuryRisk: 10f);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert
        // Stress should increase: 30 + 20 = 50
        Assert.Equal(50f, result.Stress);
        // Injury should increase: 5 + 10 = 15
        Assert.Equal(15f, result.Injury);
    }

    [Fact]
    public void Apply_AppliesBackgroundDecay_WhenTimeElapses()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-2);
        var now = DateTimeOffset.UtcNow;
        var state = CreateDefaultState(lastUpdated);
        var input = new RestInput(TimeSpan.Zero); // No rest, just decay

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - After 2 hours of decay:
        // Hunger increases: 50 + (5 * 2) = 60
        Assert.Equal(60f, result.Hunger);
        // Hydration decreases: 50 - (8 * 2) = 34
        Assert.Equal(34f, result.Hydration);
        // Fatigue increases: 50 + (10 * 2) = 70
        Assert.Equal(70f, result.Fatigue);
        // Stress increases: 50 + (3 * 2) = 56
        Assert.Equal(56f, result.Stress);
    }

    [Fact]
    public void Apply_ClampsStatsToMaxValue()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated;
        var state = CreateDefaultState(lastUpdated) with { Health = 95f };
        var input = new RestInput(TimeSpan.FromHours(10)); // Large recovery

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Health should be clamped to 100
        Assert.Equal(100f, result.Health);
    }

    [Fact]
    public void Apply_ClampsStatsToMinValue()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated;
        var state = CreateDefaultState(lastUpdated) with { Hunger = 5f };
        var input = new EatInput(Nutrition: 100f); // Large nutrition

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Hunger should be clamped to 0
        Assert.Equal(0f, result.Hunger);
    }

    [Fact]
    public void Apply_CombinesBackgroundDecayAndInput()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 50f,
            Injury: 0f,
            Stress: 30f,
            Morale: 50f,
            Hunger: 40f,
            Hydration: 60f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.FromHours(2));

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Check that both decay and rest are applied
        // Hunger after decay: 40 + (5 * 1) = 45
        Assert.Equal(45f, result.Hunger);
        // Hydration after decay: 60 - (8 * 1) = 52
        Assert.Equal(52f, result.Hydration);
        // Fatigue after decay: 50 + (10 * 1) = 60, then after rest: 60 - (20 * 2) = 20
        Assert.Equal(20f, result.Fatigue);
        // Stress after decay: 30 + (3 * 1) = 33, then after rest: 33 - (12 * 2) = 9
        Assert.Equal(9f, result.Stress);
        // Health after rest: 50 + (15 * 2) = 80
        Assert.Equal(80f, result.Health);
    }

    [Fact]
    public void Apply_IsPureFunction_DoesNotMutateOriginalState()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated.AddHours(1);
        var originalState = CreateDefaultState(lastUpdated);
        var originalHealth = originalState.Health;
        var input = new RestInput(TimeSpan.FromHours(1));

        // Act
        var result = PetRules.Apply(originalState, input, now);

        // Assert - Original state should be unchanged
        Assert.Equal(originalHealth, originalState.Health);
        Assert.Equal(lastUpdated, originalState.LastUpdated);
        Assert.NotEqual(originalState.Health, result.Health);
        Assert.NotEqual(originalState.LastUpdated, result.LastUpdated);
    }

    [Fact]
    public void Apply_WithZeroElapsedTime_OnlyAppliesInput()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated; // No time elapsed
        var state = CreateDefaultState(lastUpdated);
        var input = new EatInput(Nutrition: 20f);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Only eating effect, no decay
        Assert.Equal(30f, result.Hunger); // 50 - 20 = 30
        Assert.Equal(50f, result.Hydration); // No change
        Assert.Equal(50f, result.Fatigue); // No change
        Assert.Equal(50f, result.Stress); // No change
    }

    [Fact]
    public void Apply_WithMultipleStatsAtMax_RemainsAtMax()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 100f,
            Fatigue: 100f,
            Injury: 100f,
            Stress: 100f,
            Morale: 100f,
            Hunger: 100f,
            Hydration: 100f,
            LastUpdated: lastUpdated
        );
        var input = new MissionInput(StressLoad: 50f, InjuryRisk: 50f);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Stats should remain at max
        Assert.Equal(100f, result.Stress);
        Assert.Equal(100f, result.Injury);
    }

    [Fact]
    public void Apply_WithMultipleStatsAtMin_RemainsAtMin()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 0f,
            Fatigue: 0f,
            Injury: 0f,
            Stress: 0f,
            Morale: 0f,
            Hunger: 0f,
            Hydration: 0f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.FromHours(5));

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Stats should remain at min or increase but not go below 0
        Assert.True(result.Health >= 0f);
        Assert.True(result.Fatigue >= 0f);
        Assert.True(result.Stress >= 0f);
        Assert.Equal(0f, result.Hunger); // Already at min
    }

    [Fact]
    public void Apply_IsDeterministic_ProducesSameResultForSameInputs()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated.AddHours(1);
        var state = CreateDefaultState(lastUpdated);
        var input = new RestInput(TimeSpan.FromHours(1));

        // Act
        var result1 = PetRules.Apply(state, input, now);
        var result2 = PetRules.Apply(state, input, now);

        // Assert - Results should be identical
        Assert.Equal(result1, result2);
        Assert.Equal(result1.Health, result2.Health);
        Assert.Equal(result1.Fatigue, result2.Fatigue);
        Assert.Equal(result1.Stress, result2.Stress);
        Assert.Equal(result1.LastUpdated, result2.LastUpdated);
    }

    [Fact]
    public void Apply_PreservesUnaffectedStats()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated;
        var state = CreateDefaultState(lastUpdated) with { Morale = 75f };
        var input = new EatInput(Nutrition: 10f);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Morale and other unaffected stats should be preserved
        Assert.Equal(75f, result.Morale);
        Assert.Equal(state.OperatorId, result.OperatorId);
    }

    // ========================================
    // Conditional Decay Tests
    // ========================================

    [Fact]
    public void Apply_FatigueIncreasesFaster_WhenStressIsHigh()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 0f,
            Stress: 70f, // Above HighStressThreshold (60)
            Morale: 50f,
            Hunger: 50f,
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Fatigue increases by base rate + high stress rate
        // Expected: 20 + (10 * 1) + (5 * 1) = 35
        Assert.Equal(35f, result.Fatigue);
    }

    [Fact]
    public void Apply_FatigueIncreasesNormally_WhenStressIsNotHigh()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 0f,
            Stress: 50f, // Below HighStressThreshold (60)
            Morale: 50f,
            Hunger: 50f,
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Fatigue increases by base rate only
        // Expected: 20 + (10 * 1) = 30
        Assert.Equal(30f, result.Fatigue);
    }

    [Fact]
    public void Apply_StressIncreasesFaster_WhenInjuryIsHigh()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 50f, // Above HighInjuryThreshold (40)
            Stress: 20f,
            Morale: 50f,
            Hunger: 50f,
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Stress increases by base rate + high injury rate
        // Expected: 20 + (3 * 1) + (4 * 1) = 27
        Assert.Equal(27f, result.Stress);
    }

    [Fact]
    public void Apply_MoraleDecreases_WhenStressIsElevated()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 0f,
            Stress: 60f, // Above MoraleDecayStressThreshold (50)
            Morale: 50f,
            Hunger: 50f,
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Morale decreases
        // Expected: 50 - (2 * 1) = 48
        Assert.Equal(48f, result.Morale);
    }

    [Fact]
    public void Apply_MoraleStable_WhenStressIsLow()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 0f,
            Stress: 40f, // Below MoraleDecayStressThreshold (50)
            Morale: 50f,
            Hunger: 50f,
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Morale should not decrease
        Assert.Equal(50f, result.Morale);
    }

    [Fact]
    public void Apply_HealthDecays_WhenHungerIsCritical()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 0f,
            Stress: 30f,
            Morale: 50f,
            Hunger: 85f, // Above CriticalHungerThreshold (80)
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Health should decrease
        // Expected: 50 - (3 * 1) = 47
        Assert.Equal(47f, result.Health);
    }

    [Fact]
    public void Apply_HealthDecays_WhenHydrationIsCritical()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 0f,
            Stress: 30f,
            Morale: 50f,
            Hunger: 50f,
            Hydration: 85f, // Above CriticalHydrationThreshold (80)
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Health should decrease
        // Expected: 50 - (3 * 1) = 47
        Assert.Equal(47f, result.Health);
    }

    [Fact]
    public void Apply_HealthDecays_WhenInjuryIsCritical()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 70f, // Above CriticalInjuryThreshold (60)
            Stress: 30f,
            Morale: 50f,
            Hunger: 50f,
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Health should decrease
        // Expected: 50 - (3 * 1) = 47
        Assert.Equal(47f, result.Health);
    }

    [Fact]
    public void Apply_HealthStable_WhenNoCriticalConditions()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 50f, // Below CriticalInjuryThreshold (60)
            Stress: 30f,
            Morale: 50f,
            Hunger: 70f, // Below CriticalHungerThreshold (80)
            Hydration: 70f, // Below CriticalHydrationThreshold (80)
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Health should not decrease
        Assert.Equal(50f, result.Health);
    }

    [Fact]
    public void Apply_MoraleDecreasesFaster_DuringHealthDecay()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 70f, // Critical injury triggers health decay
            Stress: 60f, // Above MoraleDecayStressThreshold, so morale decays from stress too
            Morale: 50f,
            Hunger: 50f,
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Morale decreases from both stress and health decay
        // Expected: 50 - (2 * 1) - (3 * 1) = 45
        Assert.Equal(45f, result.Morale);
    }

    [Fact]
    public void Apply_ConditionalDecay_WorksWithFractionalHours()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-0.5);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 0f,
            Stress: 70f, // High stress
            Morale: 50f,
            Hunger: 50f,
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Fatigue increases by (10 + 5) * 0.5 = 7.5
        // Expected: 20 + 7.5 = 27.5
        Assert.Equal(27.5f, result.Fatigue);
    }

    [Fact]
    public void Apply_ConditionalDecay_ClampsAtEnd()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-10);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 80f,
            Injury: 0f,
            Stress: 80f, // High stress - would cause large fatigue increase
            Morale: 50f,
            Hunger: 50f,
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Fatigue should be clamped at 100 despite large increase
        Assert.Equal(100f, result.Fatigue);
    }

    [Fact]
    public void Apply_MultipleConditionalEffects_ApplySimultaneously()
    {
        // Arrange
        var lastUpdated = DateTimeOffset.UtcNow.AddHours(-1);
        var now = DateTimeOffset.UtcNow;
        var state = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50f,
            Fatigue: 20f,
            Injury: 70f, // Triggers: stress increase + health decay
            Stress: 70f, // Triggers: fatigue increase + morale decay
            Morale: 50f,
            Hunger: 85f, // Triggers: health decay
            Hydration: 50f,
            LastUpdated: lastUpdated
        );
        var input = new RestInput(TimeSpan.Zero);

        // Act
        var result = PetRules.Apply(state, input, now);

        // Assert - Multiple effects should compound
        // Fatigue: 20 + 10 + 5 = 35 (base + high stress)
        Assert.Equal(35f, result.Fatigue);
        // Stress: 70 + 3 + 4 = 77 (base + high injury)
        Assert.Equal(77f, result.Stress);
        // Health: 50 - 3 = 47 (health decay from injury + hunger)
        Assert.Equal(47f, result.Health);
        // Morale: 50 - 2 - 3 = 45 (stress decay + health decay)
        Assert.Equal(45f, result.Morale);
    }
}
