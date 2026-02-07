using GUNRPG.Core.Operators;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for LiteDbOperatorEventStore with focus on hash chain verification and rollback.
/// </summary>
public class LiteDbOperatorEventStoreTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly LiteDbOperatorEventStore _store;
    private readonly string _dbPath;

    public LiteDbOperatorEventStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_operator_events_{Guid.NewGuid()}.db");
        _database = new LiteDatabase(_dbPath);
        _store = new LiteDbOperatorEventStore(_database);
    }

    public void Dispose()
    {
        _database.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task AppendEvent_StoresEventSuccessfully()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var @event = new OperatorCreated(operatorId, "TestOperator");

        // Act
        await _store.AppendEventAsync(@event);

        // Assert
        var (events, rolledBack) = await _store.LoadEventsAsync(operatorId);
        Assert.Single(events);
        Assert.False(rolledBack);
        Assert.Equal(operatorId, events[0].OperatorId);
        Assert.Equal("TestOperator", ((OperatorCreated)events[0]).Name);
    }

    [Fact]
    public async Task LoadEvents_WithValidChain_ReturnsAllEvents()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
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

        // Act
        await _store.AppendEventAsync(event1);
        await _store.AppendEventAsync(event2);
        await _store.AppendEventAsync(event3);

        var (events, rolledBack) = await _store.LoadEventsAsync(operatorId);

        // Assert
        Assert.Equal(3, events.Count);
        Assert.False(rolledBack);
        Assert.Equal(1, events[0].SequenceNumber);
        Assert.Equal(2, events[1].SequenceNumber);
        Assert.Equal(3, events[2].SequenceNumber);
    }

    [Fact]
    public async Task LoadEvents_WithCorruptedHash_RollsBack()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
        var event1 = new OperatorCreated(operatorId, "TestOperator");
        var event2 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 100,
            newExfilStreak: 1,
            sequenceNumber: 2,
            previousHash: event1.Hash);

        // Store valid events
        await _store.AppendEventAsync(event1);
        await _store.AppendEventAsync(event2);

        // Corrupt the hash of event2 directly in the database
        var collection = _database.GetCollection("operator_events");
        var corruptedDoc = collection.FindOne(Query.EQ("SequenceNumber", 2L));
        corruptedDoc["Hash"] = "CORRUPTED_HASH_VALUE";
        collection.Update(corruptedDoc);

        // Act
        var (events, rolledBack) = await _store.LoadEventsAsync(operatorId);

        // Assert
        Assert.Single(events); // Only event1 should remain
        Assert.True(rolledBack); // Rollback occurred
        Assert.Equal(1, events[0].SequenceNumber);

        // Verify corrupted event was deleted
        var remainingDocs = collection.Find(Query.EQ("OperatorId", operatorId)).ToList();
        Assert.Single(remainingDocs);
    }

    [Fact]
    public async Task LoadEvents_WithBrokenChainLink_RollsBack()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
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
            previousHash: "WRONG_PREVIOUS_HASH"); // Broken link

        // Act
        await _store.AppendEventAsync(event1);
        await _store.AppendEventAsync(event2);
        await _store.AppendEventAsync(event3);

        var (events, rolledBack) = await _store.LoadEventsAsync(operatorId);

        // Assert
        Assert.Equal(2, events.Count); // Only event1 and event2 should remain
        Assert.True(rolledBack); // Rollback occurred
        Assert.Equal(1, events[0].SequenceNumber);
        Assert.Equal(2, events[1].SequenceNumber);

        // Verify event3 was deleted
        var remainingDocs = _database.GetCollection("operator_events")
            .Find(Query.EQ("OperatorId", operatorId)).ToList();
        Assert.Equal(2, remainingDocs.Count);
    }

    [Fact]
    public async Task GetCurrentSequenceNumber_WithNoEvents_ReturnsZero()
    {
        // Arrange
        var operatorId = Guid.NewGuid();

        // Act
        var sequenceNumber = await _store.GetCurrentSequenceNumberAsync(operatorId);

        // Assert
        Assert.Equal(0, sequenceNumber);
    }

    [Fact]
    public async Task GetCurrentSequenceNumber_WithEvents_ReturnsLastSequence()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
        var event1 = new OperatorCreated(operatorId, "TestOperator");
        var event2 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 100,
            newExfilStreak: 1,
            sequenceNumber: 2,
            previousHash: event1.Hash);

        // Act
        await _store.AppendEventAsync(event1);
        await _store.AppendEventAsync(event2);

        var sequenceNumber = await _store.GetCurrentSequenceNumberAsync(operatorId);

        // Assert
        Assert.Equal(2, sequenceNumber);
    }

    [Fact]
    public async Task Exists_WithNoEvents_ReturnsFalse()
    {
        // Arrange
        var operatorId = Guid.NewGuid();

        // Act
        var exists = await _store.ExistsAsync(operatorId);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task Exists_WithEvents_ReturnsTrue()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var @event = new OperatorCreated(operatorId, "TestOperator");

        // Act
        await _store.AppendEventAsync(@event);
        var exists = await _store.ExistsAsync(operatorId);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task MultipleOperators_EventsAreIsolated()
    {
        // Arrange
        var operator1Id = Guid.NewGuid();
        var operator2Id = Guid.NewGuid();
        var event1 = new OperatorCreated(operator1Id, "Operator1");
        var event2 = new OperatorCreated(operator2Id, "Operator2");

        // Act
        await _store.AppendEventAsync(event1);
        await _store.AppendEventAsync(event2);

        var (events1, _) = await _store.LoadEventsAsync(operator1Id);
        var (events2, _) = await _store.LoadEventsAsync(operator2Id);

        // Assert
        Assert.Single(events1);
        Assert.Single(events2);
        Assert.Equal("Operator1", ((OperatorCreated)events1[0]).Name);
        Assert.Equal("Operator2", ((OperatorCreated)events2[0]).Name);
    }

    [Fact]
    public async Task LoadEvents_WithPartialCorruption_PreservesValidEvents()
    {
        // Arrange
        var operatorId = Guid.NewGuid();
        var combatSessionId = Guid.NewGuid();
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
        var event4 = new ExfilSucceeded(
            operatorId,
            combatSessionId,
            experienceGained: 200,
            newExfilStreak: 3,
            sequenceNumber: 4,
            previousHash: event3.Hash);

        // Store all events
        await _store.AppendEventAsync(event1);
        await _store.AppendEventAsync(event2);
        await _store.AppendEventAsync(event3);
        await _store.AppendEventAsync(event4);

        // Corrupt event3's hash
        var collection = _database.GetCollection("operator_events");
        var corruptedDoc = collection.FindOne(Query.EQ("SequenceNumber", 3L));
        corruptedDoc["Hash"] = "CORRUPTED";
        collection.Update(corruptedDoc);

        // Act
        var (events, rolledBack) = await _store.LoadEventsAsync(operatorId);

        // Assert
        Assert.Equal(2, events.Count); // event1 and event2 preserved
        Assert.True(rolledBack);
        Assert.Equal(1, events[0].SequenceNumber);
        Assert.Equal(2, events[1].SequenceNumber);

        // Verify events 3 and 4 were deleted
        var remainingDocs = collection.Find(Query.EQ("OperatorId", operatorId)).ToList();
        Assert.Equal(2, remainingDocs.Count);
    }
}
