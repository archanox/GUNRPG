using GUNRPG.Core.VirtualPet;
using Xunit;

namespace GUNRPG.Tests;

public class OperatorStatusViewTests
{
    [Fact]
    public void From_CreatesView_WithAllPhysicalAttributes()
    {
        // Arrange
        var petState = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 75.0f,
            Fatigue: 30.0f,
            Injury: 10.0f,
            Stress: 40.0f,
            Morale: 80.0f,
            Hunger: 25.0f,
            Hydration: 60.0f,
            LastUpdated: DateTimeOffset.UtcNow
        );

        // Act
        var view = OperatorStatusView.From(petState);

        // Assert - Physical
        Assert.Equal(75.0f, view.Health);
        Assert.Equal(10.0f, view.Injury);
        Assert.Equal(30.0f, view.Fatigue);
    }

    [Fact]
    public void From_CreatesView_WithAllMentalAttributes()
    {
        // Arrange
        var petState = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 75.0f,
            Fatigue: 30.0f,
            Injury: 10.0f,
            Stress: 40.0f,
            Morale: 80.0f,
            Hunger: 25.0f,
            Hydration: 60.0f,
            LastUpdated: DateTimeOffset.UtcNow
        );

        // Act
        var view = OperatorStatusView.From(petState);

        // Assert - Mental
        Assert.Equal(40.0f, view.Stress);
        Assert.Equal(80.0f, view.Morale);
    }

    [Fact]
    public void From_CreatesView_WithAllCareAttributes()
    {
        // Arrange
        var petState = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 75.0f,
            Fatigue: 30.0f,
            Injury: 10.0f,
            Stress: 40.0f,
            Morale: 80.0f,
            Hunger: 25.0f,
            Hydration: 60.0f,
            LastUpdated: DateTimeOffset.UtcNow
        );

        // Act
        var view = OperatorStatusView.From(petState);

        // Assert - Care
        Assert.Equal(25.0f, view.Hunger);
        Assert.Equal(60.0f, view.Hydration);
    }

    [Fact]
    public void From_SetsCombatReadiness_ToNull()
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

        // Act
        var view = OperatorStatusView.From(petState);

        // Assert
        Assert.Null(view.CombatReadiness);
    }

    [Fact]
    public void From_IsDeterministic_ProducesSameOutput()
    {
        // Arrange
        var petState = new PetState(
            OperatorId: Guid.NewGuid(),
            Health: 50.0f,
            Fatigue: 60.0f,
            Injury: 20.0f,
            Stress: 70.0f,
            Morale: 40.0f,
            Hunger: 80.0f,
            Hydration: 30.0f,
            LastUpdated: DateTimeOffset.UtcNow
        );

        // Act
        var view1 = OperatorStatusView.From(petState);
        var view2 = OperatorStatusView.From(petState);

        // Assert - Multiple calls produce equal results
        Assert.Equal(view1, view2);
        Assert.Equal(view1.Health, view2.Health);
        Assert.Equal(view1.Injury, view2.Injury);
        Assert.Equal(view1.Fatigue, view2.Fatigue);
        Assert.Equal(view1.Stress, view2.Stress);
        Assert.Equal(view1.Morale, view2.Morale);
        Assert.Equal(view1.Hunger, view2.Hunger);
        Assert.Equal(view1.Hydration, view2.Hydration);
        Assert.Equal(view1.CombatReadiness, view2.CombatReadiness);
    }

    [Fact]
    public void From_HandlesMinimumValues()
    {
        // Arrange
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

        // Act
        var view = OperatorStatusView.From(petState);

        // Assert
        Assert.Equal(0.0f, view.Health);
        Assert.Equal(0.0f, view.Injury);
        Assert.Equal(0.0f, view.Fatigue);
        Assert.Equal(0.0f, view.Stress);
        Assert.Equal(0.0f, view.Morale);
        Assert.Equal(0.0f, view.Hunger);
        Assert.Equal(0.0f, view.Hydration);
    }

    [Fact]
    public void From_HandlesMaximumValues()
    {
        // Arrange
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

        // Act
        var view = OperatorStatusView.From(petState);

        // Assert
        Assert.Equal(100.0f, view.Health);
        Assert.Equal(100.0f, view.Injury);
        Assert.Equal(100.0f, view.Fatigue);
        Assert.Equal(100.0f, view.Stress);
        Assert.Equal(100.0f, view.Morale);
        Assert.Equal(100.0f, view.Hunger);
        Assert.Equal(100.0f, view.Hydration);
    }

    [Fact]
    public void OperatorStatusView_SupportsValueEquality()
    {
        // Arrange
        var view1 = new OperatorStatusView(
            Health: 100.0f,
            Injury: 0.0f,
            Fatigue: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            CombatReadiness: null
        );

        var view2 = new OperatorStatusView(
            Health: 100.0f,
            Injury: 0.0f,
            Fatigue: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            CombatReadiness: null
        );

        // Assert
        Assert.Equal(view1, view2);
        Assert.True(view1 == view2);
    }

    [Fact]
    public void OperatorStatusView_IsImmutable()
    {
        // Arrange
        var view = new OperatorStatusView(
            Health: 100.0f,
            Injury: 0.0f,
            Fatigue: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            CombatReadiness: null
        );

        // Act - Verify we can only create new instances with 'with' expression
        var newView = view with { Health = 50.0f };

        // Assert
        Assert.Equal(100.0f, view.Health);
        Assert.Equal(50.0f, newView.Health);
    }

    [Fact]
    public void OperatorStatusView_DoesNotIncludeOperatorId()
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

        // Act
        var view = OperatorStatusView.From(petState);

        // Assert - View should not expose OperatorId (presentation layer concern)
        var properties = typeof(OperatorStatusView).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name == "OperatorId");
    }

    [Fact]
    public void OperatorStatusView_DoesNotIncludeTimestamp()
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

        // Act
        var view = OperatorStatusView.From(petState);

        // Assert - View should not expose timestamp (presentation layer concern)
        var properties = typeof(OperatorStatusView).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name == "LastUpdated");
    }
}
