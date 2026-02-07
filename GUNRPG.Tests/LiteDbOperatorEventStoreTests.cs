using GUNRPG.Application.Operators;
using GUNRPG.Core.Operators;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Xunit;

namespace GUNRPG.Tests;

public class LiteDbOperatorEventStoreTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly LiteDbOperatorEventStore _store;

    public LiteDbOperatorEventStoreTests()
    {
        // Use in-memory database for testing
        _database = new LiteDatabase(":memory:");
        _store = new LiteDbOperatorEventStore(_database);
    }

    public void Dispose()
    {
        _database?.Dispose();
    }

    [Fact]
    public async Task AppendEventAsync_ShouldStoreEvent()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "TestOperator");

        // Act
        await _store.AppendEventAsync(evt);

        // Assert
        var events = await _store.LoadEventsAsync(operatorId);
        Assert.Single(events);
        Assert.Equal(operatorId, events[0].OperatorId);
    }

    [Fact]
    public async Task AppendEventAsync_ShouldRejectDuplicateSequence()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new OperatorCreatedEvent(operatorId, "Duplicate"); // Same sequence

        // Act
        await _store.AppendEventAsync(evt1);

        // Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.AppendEventAsync(evt2));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task AppendEventAsync_ShouldRejectMissingPreviousEvent()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new XpGainedEvent(operatorId, 1, 100, "Victory", "fake_hash");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.AppendEventAsync(evt));
        Assert.Contains("Previous event not found", ex.Message);
    }

    [Fact]
    public async Task AppendEventAsync_ShouldRejectBrokenHashChain()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new XpGainedEvent(operatorId, 1, 100, "Victory", "wrong_hash");

        // Act
        await _store.AppendEventAsync(evt1);

        // Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.AppendEventAsync(evt2));
        Assert.Contains("Hash chain broken", ex.Message);
    }

    [Fact]
    public async Task LoadEventsAsync_ShouldReturnEmptyForNonexistentOperator()
    {
        // Arrange
        var operatorId = OperatorId.NewId();

        // Act
        var events = await _store.LoadEventsAsync(operatorId);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public async Task LoadEventsAsync_ShouldReturnEventsInOrder()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new XpGainedEvent(operatorId, 1, 100, "Victory", evt1.Hash);
        var evt3 = new XpGainedEvent(operatorId, 2, 50, "Survived", evt2.Hash);

        // Act
        await _store.AppendEventAsync(evt1);
        await _store.AppendEventAsync(evt2);
        await _store.AppendEventAsync(evt3);

        var events = await _store.LoadEventsAsync(operatorId);

        // Assert
        Assert.Equal(3, events.Count);
        Assert.Equal(0, events[0].SequenceNumber);
        Assert.Equal(1, events[1].SequenceNumber);
        Assert.Equal(2, events[2].SequenceNumber);
    }

    [Fact]
    public async Task LoadEventsAsync_ShouldVerifyHashChain()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "TestOperator");
        await _store.AppendEventAsync(evt);

        // Manually corrupt the hash in the database
        var collection = _database.GetCollection<OperatorEventDocument>("operator_events");
        var doc = collection.FindOne(d => d.OperatorId == operatorId.Value);
        doc.Hash = "corrupted_hash";
        collection.Update(doc);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.LoadEventsAsync(operatorId));
        Assert.Contains("Corrupted event", ex.Message);
    }

    [Fact]
    public async Task OperatorExistsAsync_ShouldReturnTrueForExistingOperator()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "TestOperator");
        await _store.AppendEventAsync(evt);

        // Act
        var exists = await _store.OperatorExistsAsync(operatorId);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task OperatorExistsAsync_ShouldReturnFalseForNonexistentOperator()
    {
        // Arrange
        var operatorId = OperatorId.NewId();

        // Act
        var exists = await _store.OperatorExistsAsync(operatorId);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task GetCurrentSequenceAsync_ShouldReturnMinusOneForNonexistentOperator()
    {
        // Arrange
        var operatorId = OperatorId.NewId();

        // Act
        var sequence = await _store.GetCurrentSequenceAsync(operatorId);

        // Assert
        Assert.Equal(-1, sequence);
    }

    [Fact]
    public async Task GetCurrentSequenceAsync_ShouldReturnLatestSequence()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "TestOperator");
        var evt2 = new XpGainedEvent(operatorId, 1, 100, "Victory", evt1.Hash);
        var evt3 = new XpGainedEvent(operatorId, 2, 50, "Survived", evt2.Hash);
        await _store.AppendEventAsync(evt1);
        await _store.AppendEventAsync(evt2);
        await _store.AppendEventAsync(evt3);

        // Act
        var sequence = await _store.GetCurrentSequenceAsync(operatorId);

        // Assert
        Assert.Equal(2, sequence);
    }

    [Fact]
    public async Task ListOperatorIdsAsync_ShouldReturnAllOperators()
    {
        // Arrange
        var op1 = OperatorId.NewId();
        var op2 = OperatorId.NewId();
        await _store.AppendEventAsync(new OperatorCreatedEvent(op1, "Operator1"));
        await _store.AppendEventAsync(new OperatorCreatedEvent(op2, "Operator2"));

        // Act
        var operatorIds = await _store.ListOperatorIdsAsync();

        // Assert
        Assert.Equal(2, operatorIds.Count);
        Assert.Contains(op1, operatorIds);
        Assert.Contains(op2, operatorIds);
    }

    [Fact]
    public async Task EventStore_ShouldSupportMultipleOperators()
    {
        // Arrange
        var op1 = OperatorId.NewId();
        var op2 = OperatorId.NewId();
        
        var evt1_1 = new OperatorCreatedEvent(op1, "Operator1");
        var evt1_2 = new XpGainedEvent(op1, 1, 100, "Victory", evt1_1.Hash);
        
        var evt2_1 = new OperatorCreatedEvent(op2, "Operator2");
        var evt2_2 = new XpGainedEvent(op2, 1, 50, "Survived", evt2_1.Hash);

        // Act
        await _store.AppendEventAsync(evt1_1);
        await _store.AppendEventAsync(evt1_2);
        await _store.AppendEventAsync(evt2_1);
        await _store.AppendEventAsync(evt2_2);

        var events1 = await _store.LoadEventsAsync(op1);
        var events2 = await _store.LoadEventsAsync(op2);

        // Assert
        Assert.Equal(2, events1.Count);
        Assert.Equal(2, events2.Count);
        Assert.Equal(100, ((XpGainedEvent)events1[1]).GetPayload().XpAmount);
        Assert.Equal(50, ((XpGainedEvent)events2[1]).GetPayload().XpAmount);
    }
}
