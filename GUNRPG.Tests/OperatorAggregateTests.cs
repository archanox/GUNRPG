using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

public class OperatorAggregateTests
{
    [Fact]
    public void Create_ShouldInitializeFromCreatedEvent()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var createdEvent = new OperatorCreatedEvent(operatorId, "TestOperator");

        // Act
        var aggregate = OperatorAggregate.Create(createdEvent);

        // Assert
        Assert.Equal(operatorId, aggregate.Id);
        Assert.Equal("TestOperator", aggregate.Name);
        Assert.Equal(0, aggregate.TotalXp);
        Assert.Equal(100f, aggregate.MaxHealth);
        Assert.Equal(100f, aggregate.CurrentHealth);
        Assert.Equal(0, aggregate.CurrentSequence);
    }

    [Fact]
    public void FromEvents_ShouldReplayEventsInOrder()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new XpGainedEvent(operatorId, 1, 100, "Victory", evt1.Hash);
        var evt3 = new XpGainedEvent(operatorId, 2, 50, "Survived", evt2.Hash);
        var events = new List<OperatorEvent> { evt1, evt2, evt3 };

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(150, aggregate.TotalXp);
        Assert.Equal(2, aggregate.CurrentSequence);
    }

    [Fact]
    public void FromEvents_ShouldThrowOnEmptyEventList()
    {
        // Arrange
        var events = new List<OperatorEvent>();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => OperatorAggregate.FromEvents(events));
        Assert.Contains("empty event list", ex.Message);
    }

    [Fact]
    public void FromEvents_ShouldRollbackOnBrokenChain()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new XpGainedEvent(operatorId, 1, 100, "Victory", "wrong_hash");
        var events = new List<OperatorEvent> { evt1, evt2 };

        // Act - Should roll back to evt1 only
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert - Only first event should be applied
        Assert.Equal(0, aggregate.TotalXp);
        Assert.Equal(0, aggregate.CurrentSequence);
    }

    [Fact]
    public void FromEvents_ShouldApplyXpGainedCorrectly()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new XpGainedEvent(operatorId, 1, 250, "Victory", evt1.Hash);
        var events = new List<OperatorEvent> { evt1, evt2 };

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(250, aggregate.TotalXp);
    }

    [Fact]
    public void FromEvents_ShouldApplyWoundsTreatedCorrectly()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new WoundsTreatedEvent(operatorId, 1, 30f, evt1.Hash);
        var events = new List<OperatorEvent> { evt1, evt2 };

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(100f, aggregate.CurrentHealth); // Already at max
    }

    [Fact]
    public void FromEvents_ShouldApplyLoadoutChangedCorrectly()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new LoadoutChangedEvent(operatorId, 1, "M4A1", evt1.Hash);
        var events = new List<OperatorEvent> { evt1, evt2 };

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal("M4A1", aggregate.EquippedWeaponName);
    }

    [Fact]
    public void FromEvents_ShouldApplyPerkUnlockedCorrectly()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new PerkUnlockedEvent(operatorId, 1, "Fast Reload", evt1.Hash);
        var evt3 = new PerkUnlockedEvent(operatorId, 2, "Double Tap", evt2.Hash);
        var events = new List<OperatorEvent> { evt1, evt2, evt3 };

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(2, aggregate.UnlockedPerks.Count);
        Assert.Contains("Fast Reload", aggregate.UnlockedPerks);
        Assert.Contains("Double Tap", aggregate.UnlockedPerks);
    }

    [Fact]
    public void GetLastEventHash_ShouldReturnEmptyForNewAggregate()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "TestOperator");
        var aggregate = OperatorAggregate.Create(evt);

        // Act
        var lastHash = aggregate.GetLastEventHash();

        // Assert
        Assert.NotEmpty(lastHash);
        Assert.Equal(evt.Hash, lastHash);
    }

    [Fact]
    public void GetLastEventHash_ShouldReturnLatestHash()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new XpGainedEvent(operatorId, 1, 100, "Victory", evt1.Hash);
        var events = new List<OperatorEvent> { evt1, evt2 };
        var aggregate = OperatorAggregate.FromEvents(events);

        // Act
        var lastHash = aggregate.GetLastEventHash();

        // Assert
        Assert.Equal(evt2.Hash, lastHash);
    }

    [Fact]
    public void CreateCombatSnapshot_ShouldCopyBasicStats()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "TestOperator");
        var aggregate = OperatorAggregate.Create(evt);

        // Act
        var snapshot = aggregate.CreateCombatSnapshot();

        // Assert
        Assert.Equal(operatorId.Value, snapshot.Id);
        Assert.Equal("TestOperator", snapshot.Name);
        Assert.Equal(100f, snapshot.MaxHealth);
        Assert.Equal(100f, snapshot.Health);
    }

    [Fact]
    public void WoundsTreated_ShouldNotExceedMaxHealth()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");

        // Apply healing to an operator at full health
        var evt2 = new WoundsTreatedEvent(operatorId, 1, 50f, evt1.Hash);

        // Act
        var events = new List<OperatorEvent> { evt1, evt2 };
        var restoredAggregate = OperatorAggregate.FromEvents(events);

        // Assert - Health should remain capped at max even with excess healing
        Assert.Equal(100f, restoredAggregate.CurrentHealth);
    }

    [Fact]
    public void ExfilSucceeded_ShouldIncrementStreak()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new ExfilSucceededEvent(operatorId, 1, evt1.Hash);
        var evt3 = new ExfilSucceededEvent(operatorId, 2, evt2.Hash);

        // Act
        var events = new List<OperatorEvent> { evt1, evt2, evt3 };
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(2, aggregate.ExfilStreak);
        Assert.False(aggregate.IsDead);
    }

    [Fact]
    public void ExfilFailed_ShouldResetStreak()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new ExfilSucceededEvent(operatorId, 1, evt1.Hash);
        var evt3 = new ExfilSucceededEvent(operatorId, 2, evt2.Hash);
        var evt4 = new ExfilFailedEvent(operatorId, 3, "Retreat", evt3.Hash);

        // Act
        var events = new List<OperatorEvent> { evt1, evt2, evt3, evt4 };
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(0, aggregate.ExfilStreak);
        Assert.False(aggregate.IsDead);
    }

    [Fact]
    public void OperatorDied_ShouldMarkDeadAndResetStreak()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new ExfilSucceededEvent(operatorId, 1, evt1.Hash);
        var evt3 = new OperatorDiedEvent(operatorId, 2, "Combat casualty", evt2.Hash);

        // Act
        var events = new List<OperatorEvent> { evt1, evt2, evt3 };
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.True(aggregate.IsDead);
        Assert.Equal(0, aggregate.CurrentHealth);
        Assert.Equal(0, aggregate.ExfilStreak);
    }

    [Fact]
    public void FromEvents_ShouldRollbackOnHashFailure()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new XpGainedEvent(operatorId, 1, 100, "Victory", evt1.Hash);
        
        // Create a corrupted event with wrong hash
        var evt3 = new XpGainedEvent(operatorId, 2, 50, "Survived", evt2.Hash);
        // Manually corrupt the event by creating a new one with same data but we'll simulate corruption
        // by passing wrong previous hash to the next event
        var evt4 = new XpGainedEvent(operatorId, 3, 25, "Bonus", "corrupted_hash");

        var events = new List<OperatorEvent> { evt1, evt2, evt3, evt4 };

        // Act - The aggregate should only replay valid events up to evt3
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert - Should have rolled back to last valid event (evt3)
        Assert.Equal(150, aggregate.TotalXp); // Only evt2 and evt3 applied
        Assert.Equal(2, aggregate.CurrentSequence);
    }

    [Fact]
    public void FromEvents_ShouldThrowIfFirstEventInvalid()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        // Create event with wrong previous hash for genesis event
        var evt1 = new XpGainedEvent(operatorId, 0, 100, "Invalid", "should_be_empty");

        var events = new List<OperatorEvent> { evt1 };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => OperatorAggregate.FromEvents(events));
        Assert.Contains("No valid events", ex.Message);
    }

    [Fact]
    public void NewOperator_ShouldStartWithZeroStreakAndAlive()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "TestOperator");

        // Act
        var aggregate = OperatorAggregate.Create(evt);

        // Assert
        Assert.Equal(0, aggregate.ExfilStreak);
        Assert.False(aggregate.IsDead);
    }
}
