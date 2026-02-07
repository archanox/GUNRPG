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
    public void FromEvents_ShouldThrowOnBrokenChain()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new XpGainedEvent(operatorId, 1, 100, "Victory", "wrong_hash");
        var events = new List<OperatorEvent> { evt1, evt2 };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => OperatorAggregate.FromEvents(events));
        Assert.Contains("chain broken", ex.Message);
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
    public void TakeCombatDamage_ShouldReduceHealth()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "TestOperator");
        var aggregate = OperatorAggregate.Create(evt);

        // Act
        aggregate.TakeCombatDamage(30f);

        // Assert
        Assert.Equal(70f, aggregate.CurrentHealth);
    }

    [Fact]
    public void TakeCombatDamage_ShouldNotGoNegative()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "TestOperator");
        var aggregate = OperatorAggregate.Create(evt);

        // Act
        aggregate.TakeCombatDamage(150f);

        // Assert
        Assert.Equal(0f, aggregate.CurrentHealth);
    }

    [Fact]
    public void WoundsTreated_ShouldNotExceedMaxHealth()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var aggregate = OperatorAggregate.Create(evt1);
        aggregate.TakeCombatDamage(20f); // Health now at 80

        var evt2 = new WoundsTreatedEvent(operatorId, 1, 50f, evt1.Hash); // Try to heal 50

        // Act
        var events = new List<OperatorEvent> { evt1, evt2 };
        var restoredAggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(100f, restoredAggregate.CurrentHealth); // Capped at max
    }
}
