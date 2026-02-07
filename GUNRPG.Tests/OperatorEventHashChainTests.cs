using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for hash chain verification in operator events.
/// </summary>
public class OperatorEventHashChainTests
{
    [Fact]
    public void OperatorCreated_ComputesValidHash()
    {
        // Arrange & Act
        var operatorId = Guid.NewGuid();
        var @event = new OperatorCreated(operatorId, "TestOperator");

        // Assert
        Assert.NotNull(@event.Hash);
        Assert.NotEmpty(@event.Hash);
        Assert.True(@event.VerifyHash());
    }

    [Fact]
    public void ExfilSucceeded_ComputesValidHash()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
        var previousHash = "ABCD1234";

        // Act
        var @event = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 100,
            newExfilStreak: 1,
            sequenceNumber: 2,
            previousHash: previousHash);

        // Assert
        Assert.NotNull(@event.Hash);
        Assert.NotEmpty(@event.Hash);
        Assert.Equal(previousHash, @event.PreviousHash);
        Assert.True(@event.VerifyHash());
    }

    [Fact]
    public void ExfilFailed_ComputesValidHash()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
        var previousHash = "ABCD1234";

        // Act
        var @event = new ExfilFailed(
            operatorId,
            combatSessionId,
            reason: "Abandoned extraction",
            newExfilStreak: 0,
            sequenceNumber: 2,
            previousHash: previousHash);

        // Assert
        Assert.NotNull(@event.Hash);
        Assert.NotEmpty(@event.Hash);
        Assert.Equal(previousHash, @event.PreviousHash);
        Assert.True(@event.VerifyHash());
    }

    [Fact]
    public void OperatorDied_ComputesValidHash()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
        var previousHash = "ABCD1234";

        // Act
        var @event = new OperatorDied(
            operatorId,
            combatSessionId,
            causeOfDeath: "KIA",
            newExfilStreak: 0,
            sequenceNumber: 2,
            previousHash: previousHash);

        // Assert
        Assert.NotNull(@event.Hash);
        Assert.NotEmpty(@event.Hash);
        Assert.Equal(previousHash, @event.PreviousHash);
        Assert.True(@event.VerifyHash());
    }

    [Fact]
    public void EventChain_LinksCorrectly()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();

        // Act
        var event1 = new OperatorCreated(operatorId, "TestOperator");
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

        // Assert
        Assert.Null(event1.PreviousHash); // Genesis event
        Assert.Equal(event1.Hash, event2.PreviousHash);
        Assert.Equal(event2.Hash, event3.PreviousHash);
        Assert.True(event1.VerifyHash());
        Assert.True(event2.VerifyHash());
        Assert.True(event3.VerifyHash());
    }

    [Fact]
    public void DifferentEvents_ProduceDifferentHashes()
    {
        // Arrange
        var operatorId = Guid.NewGuid();

        // Act
        var event1 = new OperatorCreated(operatorId, "Operator1");
        var event2 = new OperatorCreated(operatorId, "Operator2");

        // Assert
        Assert.NotEqual(event1.Hash, event2.Hash);
    }
}
