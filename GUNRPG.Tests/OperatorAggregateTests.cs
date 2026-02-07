using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for the OperatorAggregate event sourcing logic.
/// </summary>
public class OperatorAggregateTests
{
    [Fact]
    public void FromEvents_WithOperatorCreated_InitializesAggregate()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var @event = new OperatorCreated(operatorId, "Ghost");
        var events = new OperatorEvent[] { @event };

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(operatorId, aggregate.Id);
        Assert.Equal("Ghost", aggregate.Name);
        Assert.Equal(0, aggregate.ExfilStreak);
        Assert.Equal(0, aggregate.TotalExperience);
        Assert.Equal(0, aggregate.SuccessfulExfils);
        Assert.Equal(0, aggregate.FailedExfils);
        Assert.Equal(0, aggregate.Deaths);
        Assert.True(aggregate.IsAlive);
        Assert.Equal(1, aggregate.CurrentSequenceNumber);
        Assert.Equal(@event.Hash, aggregate.LastEventHash);
    }

    [Fact]
    public void ExfilSucceeded_IncrementsStreak()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
        var event1 = new OperatorCreated(operatorId, "Ghost");
        var event2 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 100,
            newExfilStreak: 1,
            sequenceNumber: 2,
            previousHash: event1.Hash);

        var events = new OperatorEvent[] { event1, event2 };

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(1, aggregate.ExfilStreak);
        Assert.Equal(100, aggregate.TotalExperience);
        Assert.Equal(1, aggregate.SuccessfulExfils);
        Assert.Equal(0, aggregate.FailedExfils);
        Assert.Equal(0, aggregate.Deaths);
    }

    [Fact]
    public void MultipleExfilSucceeded_IncrementsStreakAndExperience()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
        var event1 = new OperatorCreated(operatorId, "Ghost");
        var event2 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 100,
            newExfilStreak: 1,
            sequenceNumber: 2,
            previousHash: event1.Hash);
        var event3 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 150,
            newExfilStreak: 2,
            sequenceNumber: 3,
            previousHash: event2.Hash);
        var event4 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 200,
            newExfilStreak: 3,
            sequenceNumber: 4,
            previousHash: event3.Hash);

        var events = new OperatorEvent[] { event1, event2, event3, event4 };

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(3, aggregate.ExfilStreak);
        Assert.Equal(450, aggregate.TotalExperience); // 100 + 150 + 200
        Assert.Equal(3, aggregate.SuccessfulExfils);
        Assert.Equal(0, aggregate.FailedExfils);
        Assert.Equal(0, aggregate.Deaths);
    }

    [Fact]
    public void ExfilFailed_ResetsStreak()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
        var event1 = new OperatorCreated(operatorId, "Ghost");
        var event2 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 100,
            newExfilStreak: 1,
            sequenceNumber: 2,
            previousHash: event1.Hash);
        var event3 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 150,
            newExfilStreak: 2,
            sequenceNumber: 3,
            previousHash: event2.Hash);
        var event4 = new ExfilFailed(
            operatorId,
            combatSessionId,
            reason: "Abandoned extraction",
            newExfilStreak: 0,
            sequenceNumber: 4,
            previousHash: event3.Hash);

        var events = new OperatorEvent[] { event1, event2, event3, event4 };

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(0, aggregate.ExfilStreak); // Reset to 0
        Assert.Equal(250, aggregate.TotalExperience); // Still keeps XP from successful exfils
        Assert.Equal(2, aggregate.SuccessfulExfils);
        Assert.Equal(1, aggregate.FailedExfils);
        Assert.Equal(0, aggregate.Deaths);
    }

    [Fact]
    public void OperatorDied_ResetsStreak()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
        var event1 = new OperatorCreated(operatorId, "Ghost");
        var event2 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 100,
            newExfilStreak: 1,
            sequenceNumber: 2,
            previousHash: event1.Hash);
        var event3 = new OperatorDied(
            operatorId,
            combatSessionId,
            causeOfDeath: "KIA",
            newExfilStreak: 0,
            sequenceNumber: 3,
            previousHash: event2.Hash);

        var events = new OperatorEvent[] { event1, event2, event3 };

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(0, aggregate.ExfilStreak); // Reset to 0
        Assert.Equal(100, aggregate.TotalExperience); // Keeps XP
        Assert.Equal(1, aggregate.SuccessfulExfils);
        Assert.Equal(0, aggregate.FailedExfils);
        Assert.Equal(1, aggregate.Deaths);
    }

    [Fact]
    public void StreakRecovery_AfterFailure()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
        var event1 = new OperatorCreated(operatorId, "Ghost");
        var event2 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 100,
            newExfilStreak: 1,
            sequenceNumber: 2,
            previousHash: event1.Hash);
        var event3 = new ExfilFailed(
            operatorId,
            combatSessionId,
            reason: "Abandoned",
            newExfilStreak: 0,
            sequenceNumber: 3,
            previousHash: event2.Hash);
        var event4 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 120,
            newExfilStreak: 1,
            sequenceNumber: 4,
            previousHash: event3.Hash);
        var event5 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 140,
            newExfilStreak: 2,
            sequenceNumber: 5,
            previousHash: event4.Hash);

        var events = new OperatorEvent[] { event1, event2, event3, event4, event5 };

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert
        Assert.Equal(2, aggregate.ExfilStreak); // Rebuilt streak after failure
        Assert.Equal(360, aggregate.TotalExperience); // 100 + 120 + 140
        Assert.Equal(3, aggregate.SuccessfulExfils);
        Assert.Equal(1, aggregate.FailedExfils);
        Assert.Equal(0, aggregate.Deaths);
    }

    [Fact]
    public void GetCombatSnapshot_ReturnsReadOnlyData()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
        var event1 = new OperatorCreated(operatorId, "Ghost");
        var event2 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 100,
            newExfilStreak: 1,
            sequenceNumber: 2,
            previousHash: event1.Hash);

        var events = new OperatorEvent[] { event1, event2 };
        var aggregate = OperatorAggregate.FromEvents(events);

        // Act
        var snapshot = aggregate.GetCombatSnapshot();

        // Assert
        Assert.Equal(operatorId, snapshot.OperatorId);
        Assert.Equal("Ghost", snapshot.Name);
        Assert.Equal(1, snapshot.ExfilStreak);
        Assert.Equal(100, snapshot.TotalExperience);
    }

    [Fact]
    public void EmptyEventStream_ThrowsNoException()
    {
        // Arrange
        var events = Array.Empty<OperatorEvent>();

        // Act
        var aggregate = OperatorAggregate.FromEvents(events);

        // Assert - aggregate exists but is uninitialized
        Assert.Equal(Guid.Empty, aggregate.Id);
        Assert.Equal(string.Empty, aggregate.Name);
        Assert.Equal(0, aggregate.CurrentSequenceNumber);
    }
}
