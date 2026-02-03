using GUNRPG.Core.VirtualPet;
using Xunit;

namespace GUNRPG.Tests;

public class PetStateTests
{
    [Fact]
    public void PetState_CanBeCreated_WithAllProperties()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var lastUpdated = DateTimeOffset.UtcNow;

        // Act
        var petState = new PetState(
            OperatorId: operatorId,
            Health: 75.0f,
            Fatigue: 30.0f,
            Injury: 10.0f,
            Stress: 40.0f,
            Morale: 80.0f,
            Hunger: 25.0f,
            Hydration: 60.0f,
            LastUpdated: lastUpdated
        );

        // Assert
        Assert.Equal(operatorId, petState.OperatorId);
        Assert.Equal(75.0f, petState.Health);
        Assert.Equal(30.0f, petState.Fatigue);
        Assert.Equal(10.0f, petState.Injury);
        Assert.Equal(40.0f, petState.Stress);
        Assert.Equal(80.0f, petState.Morale);
        Assert.Equal(25.0f, petState.Hunger);
        Assert.Equal(60.0f, petState.Hydration);
        Assert.Equal(lastUpdated, petState.LastUpdated);
    }

    [Fact]
    public void PetState_SupportsValueEquality()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var lastUpdated = DateTimeOffset.UtcNow;

        var petState1 = new PetState(
            operatorId,
            Health: 100.0f,
            Fatigue: 0.0f,
            Injury: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            lastUpdated
        );

        var petState2 = new PetState(
            operatorId,
            Health: 100.0f,
            Fatigue: 0.0f,
            Injury: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            lastUpdated
        );

        // Assert
        Assert.Equal(petState1, petState2);
        Assert.True(petState1 == petState2);
    }

    [Fact]
    public void PetState_SupportsWithExpression_ForImmutableUpdates()
    {
        // Arrange
        var originalState = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 100.0f,
            Fatigue: 0.0f,
            Injury: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            LastUpdated: DateTimeOffset.UtcNow
        );

        // Act - Update health using 'with' expression
        var updatedState = originalState with { Health = 50.0f };

        // Assert
        Assert.Equal(100.0f, originalState.Health); // Original unchanged
        Assert.Equal(50.0f, updatedState.Health); // New instance with updated value
        Assert.Equal(originalState.OperatorId, updatedState.OperatorId); // Other values copied
    }

    [Fact]
    public void PetState_AcceptsMinimumValues()
    {
        // Arrange & Act
        var petState = new PetState(
            OperatorId: Guid.Empty,
            Health: 0.0f,
            Fatigue: 0.0f,
            Injury: 0.0f,
            Stress: 0.0f,
            Morale: 0.0f,
            Hunger: 0.0f,
            Hydration: 0.0f,
            LastUpdated: DateTimeOffset.MinValue
        );

        // Assert
        Assert.Equal(0.0f, petState.Health);
        Assert.Equal(0.0f, petState.Fatigue);
        Assert.Equal(0.0f, petState.Injury);
        Assert.Equal(0.0f, petState.Stress);
        Assert.Equal(0.0f, petState.Morale);
        Assert.Equal(0.0f, petState.Hunger);
        Assert.Equal(0.0f, petState.Hydration);
    }

    [Fact]
    public void PetState_AcceptsMaximumValues()
    {
        // Arrange & Act
        var petState = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 100.0f,
            Fatigue: 100.0f,
            Injury: 100.0f,
            Stress: 100.0f,
            Morale: 100.0f,
            Hunger: 100.0f,
            Hydration: 100.0f,
            LastUpdated: DateTimeOffset.MaxValue
        );

        // Assert
        Assert.Equal(100.0f, petState.Health);
        Assert.Equal(100.0f, petState.Fatigue);
        Assert.Equal(100.0f, petState.Injury);
        Assert.Equal(100.0f, petState.Stress);
        Assert.Equal(100.0f, petState.Morale);
        Assert.Equal(100.0f, petState.Hunger);
        Assert.Equal(100.0f, petState.Hydration);
    }

    [Fact]
    public void PetState_HasSameHashCode_ForEqualValues()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var lastUpdated = DateTimeOffset.UtcNow;

        var petState1 = new PetState(
            operatorId,
            Health: 100.0f,
            Fatigue: 0.0f,
            Injury: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            lastUpdated
        );

        var petState2 = new PetState(
            operatorId,
            Health: 100.0f,
            Fatigue: 0.0f,
            Injury: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            lastUpdated
        );

        // Assert - Equal objects must have equal hash codes
        Assert.Equal(petState1.GetHashCode(), petState2.GetHashCode());
    }

    [Fact]
    public void PetState_IsImmutable_CannotModifyPropertiesDirectly()
    {
        // Arrange
        var petState = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 100.0f,
            Fatigue: 0.0f,
            Injury: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            LastUpdated: DateTimeOffset.UtcNow
        );

        // Act - Verify that properties are init-only
        // This test verifies immutability at compile-time
        // The following would not compile:
        // petState.Health = 50.0f;

        // Assert - Verify we can only create new instances with 'with' expression
        var newState = petState with { Health = 50.0f };
        Assert.Equal(100.0f, petState.Health);
        Assert.Equal(50.0f, newState.Health);
    }
}
