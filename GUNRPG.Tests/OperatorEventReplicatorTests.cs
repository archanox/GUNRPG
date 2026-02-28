using GUNRPG.Application.Distributed;
using GUNRPG.Application.Operators;
using GUNRPG.Core.Operators;
using GUNRPG.Infrastructure.Distributed;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for OperatorEventReplicator: verifying that operator events are distributed
/// to all connected peers so player state is accessible from any server.
/// </summary>
public class OperatorEventReplicatorTests
{
    // --- Broadcast on event append ---

    [Fact]
    public async Task BroadcastAsync_NoPeers_DoesNotThrow()
    {
        var nodeId = Guid.NewGuid();
        var transport = new InMemoryLockstepTransport(nodeId);
        var store = new InMemoryOperatorEventStore();
        var replicator = new OperatorEventReplicator(nodeId, transport, store);

        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "TestOp");

        // Should not throw even with no peers
        await replicator.BroadcastAsync(evt);
    }

    [Fact]
    public async Task BroadcastAsync_WithPeer_PeerReceivesEvent()
    {
        var nodeIdA = Guid.NewGuid();
        var nodeIdB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeIdA);
        var transportB = new InMemoryLockstepTransport(nodeIdB);
        var storeA = new InMemoryOperatorEventStore();
        var storeB = new InMemoryOperatorEventStore();

        var replicatorA = new OperatorEventReplicator(nodeIdA, transportA, storeA);
        _ = new OperatorEventReplicator(nodeIdB, transportB, storeB);

        // Connect after setting up replicators so OnPeerConnected fires
        // (no pre-existing events, so sync response is empty)
        transportA.ConnectTo(transportB);

        var operatorId = OperatorId.NewId();
        var createdEvent = new OperatorCreatedEvent(operatorId, "Alpha");

        // Append to storeA and broadcast via the replicator
        await storeA.AppendEventAsync(createdEvent);
        await replicatorA.BroadcastAsync(createdEvent);

        // storeB should now have the operator
        var events = await WaitForEventsAsync(storeB, operatorId, expectedCount: 1);
        Assert.Single(events);
        Assert.Equal("OperatorCreated", events[0].EventType);
        Assert.Equal(operatorId, events[0].OperatorId);
    }

    [Fact]
    public async Task BroadcastAsync_PeerStoresEventWithCorrectData()
    {
        var nodeIdA = Guid.NewGuid();
        var nodeIdB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeIdA);
        var transportB = new InMemoryLockstepTransport(nodeIdB);
        var storeA = new InMemoryOperatorEventStore();
        var storeB = new InMemoryOperatorEventStore();

        var replicatorA = new OperatorEventReplicator(nodeIdA, transportA, storeA);
        _ = new OperatorEventReplicator(nodeIdB, transportB, storeB);
        transportA.ConnectTo(transportB);

        var operatorId = OperatorId.NewId();
        var createdEvent = new OperatorCreatedEvent(operatorId, "Bravo");
        await storeA.AppendEventAsync(createdEvent);
        await replicatorA.BroadcastAsync(createdEvent);

        var events = await WaitForEventsAsync(storeB, operatorId, expectedCount: 1);
        Assert.Single(events);
        Assert.Equal(createdEvent.OperatorId, events[0].OperatorId);
        Assert.Equal(createdEvent.SequenceNumber, events[0].SequenceNumber);
        Assert.Equal(createdEvent.EventType, events[0].EventType);
        Assert.Equal(createdEvent.Hash, events[0].Hash);
        Assert.Equal(createdEvent.PreviousHash, events[0].PreviousHash);
    }

    [Fact]
    public async Task BroadcastAsync_MultipleEvents_PeerStoresAll()
    {
        var nodeIdA = Guid.NewGuid();
        var nodeIdB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeIdA);
        var transportB = new InMemoryLockstepTransport(nodeIdB);
        var storeA = new InMemoryOperatorEventStore();
        var storeB = new InMemoryOperatorEventStore();

        var replicatorA = new OperatorEventReplicator(nodeIdA, transportA, storeA);
        _ = new OperatorEventReplicator(nodeIdB, transportB, storeB);
        transportA.ConnectTo(transportB);

        var operatorId = OperatorId.NewId();
        var createdEvent = new OperatorCreatedEvent(operatorId, "Charlie");
        await storeA.AppendEventAsync(createdEvent);
        await replicatorA.BroadcastAsync(createdEvent);

        var xpEvent = new XpGainedEvent(operatorId, 1, 100, "Mission complete", createdEvent.Hash);
        await storeA.AppendEventAsync(xpEvent);
        await replicatorA.BroadcastAsync(xpEvent);

        var events = await WaitForEventsAsync(storeB, operatorId, expectedCount: 2);
        Assert.Equal(2, events.Count);
        Assert.Equal("OperatorCreated", events[0].EventType);
        Assert.Equal("XpGained", events[1].EventType);
    }

    // --- Sync on peer connect ---

    [Fact]
    public async Task OnPeerConnected_ExistingEvents_SyncedToPeer()
    {
        var nodeIdA = Guid.NewGuid();
        var nodeIdB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeIdA);
        var transportB = new InMemoryLockstepTransport(nodeIdB);
        var storeA = new InMemoryOperatorEventStore();
        var storeB = new InMemoryOperatorEventStore();

        // A has an operator created before B connects
        var operatorId = OperatorId.NewId();
        var createdEvent = new OperatorCreatedEvent(operatorId, "Delta");
        await storeA.AppendEventAsync(createdEvent);

        // Set up replicators then connect — should trigger sync
        _ = new OperatorEventReplicator(nodeIdA, transportA, storeA);
        _ = new OperatorEventReplicator(nodeIdB, transportB, storeB);
        transportA.ConnectTo(transportB);

        // B should now have the operator
        var events = await WaitForEventsAsync(storeB, operatorId, expectedCount: 1);
        Assert.Single(events);
        Assert.Equal("OperatorCreated", events[0].EventType);
    }

    [Fact]
    public async Task OnPeerConnected_MultipleOperators_AllSyncedToPeer()
    {
        var nodeIdA = Guid.NewGuid();
        var nodeIdB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeIdA);
        var transportB = new InMemoryLockstepTransport(nodeIdB);
        var storeA = new InMemoryOperatorEventStore();
        var storeB = new InMemoryOperatorEventStore();

        // A has two operators
        var opId1 = OperatorId.NewId();
        var opId2 = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(opId1, "Echo");
        var evt2 = new OperatorCreatedEvent(opId2, "Foxtrot");
        await storeA.AppendEventAsync(evt1);
        await storeA.AppendEventAsync(evt2);

        _ = new OperatorEventReplicator(nodeIdA, transportA, storeA);
        _ = new OperatorEventReplicator(nodeIdB, transportB, storeB);
        transportA.ConnectTo(transportB);

        Assert.Single(await WaitForEventsAsync(storeB, opId1, expectedCount: 1));
        Assert.Single(await WaitForEventsAsync(storeB, opId2, expectedCount: 1));
    }

    [Fact]
    public async Task OnPeerConnected_BothHaveEvents_BothSync()
    {
        var nodeIdA = Guid.NewGuid();
        var nodeIdB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeIdA);
        var transportB = new InMemoryLockstepTransport(nodeIdB);
        var storeA = new InMemoryOperatorEventStore();
        var storeB = new InMemoryOperatorEventStore();

        var opIdA = OperatorId.NewId();
        var opIdB = OperatorId.NewId();

        await storeA.AppendEventAsync(new OperatorCreatedEvent(opIdA, "Golf"));
        await storeB.AppendEventAsync(new OperatorCreatedEvent(opIdB, "Hotel"));

        _ = new OperatorEventReplicator(nodeIdA, transportA, storeA);
        _ = new OperatorEventReplicator(nodeIdB, transportB, storeB);
        transportA.ConnectTo(transportB);

        // A should now have B's operator and vice versa
        Assert.Single(await WaitForEventsAsync(storeA, opIdB, expectedCount: 1));
        Assert.Single(await WaitForEventsAsync(storeB, opIdA, expectedCount: 1));
    }

    // --- Duplicate handling ---

    [Fact]
    public async Task BroadcastAsync_AlreadyKnownEvent_NotAppliedAgain()
    {
        var nodeIdA = Guid.NewGuid();
        var nodeIdB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeIdA);
        var transportB = new InMemoryLockstepTransport(nodeIdB);
        var storeA = new InMemoryOperatorEventStore();
        var storeB = new InMemoryOperatorEventStore();

        var replicatorA = new OperatorEventReplicator(nodeIdA, transportA, storeA);
        _ = new OperatorEventReplicator(nodeIdB, transportB, storeB);
        transportA.ConnectTo(transportB);

        var operatorId = OperatorId.NewId();
        var createdEvent = new OperatorCreatedEvent(operatorId, "India");
        await storeA.AppendEventAsync(createdEvent);

        // Broadcast same event twice; second should be silently skipped
        await replicatorA.BroadcastAsync(createdEvent);
        await WaitForEventsAsync(storeB, operatorId, expectedCount: 1);
        await replicatorA.BroadcastAsync(createdEvent);
        // Give the second broadcast a chance to be processed
        await Task.Delay(50);

        // B should still have exactly one event (no duplicates)
        var events = await storeB.LoadEventsAsync(operatorId);
        Assert.Single(events);
    }

    // --- RehydrateEvent ---

    [Fact]
    public void RehydrateEvent_OperatorCreated_ReturnsCorrectType()
    {
        var operatorId = OperatorId.NewId();
        var original = new OperatorCreatedEvent(operatorId, "Juliet");
        var msg = CreateMessage(original);

        var result = OperatorEventReplicator.RehydrateEvent(msg);

        Assert.IsType<OperatorCreatedEvent>(result);
        Assert.Equal(original.OperatorId, result.OperatorId);
        Assert.Equal(original.SequenceNumber, result.SequenceNumber);
        Assert.Equal(original.Hash, result.Hash);
    }

    [Fact]
    public void RehydrateEvent_XpGained_ReturnsCorrectType()
    {
        var operatorId = OperatorId.NewId();
        var createdEvent = new OperatorCreatedEvent(operatorId, "Kilo");
        var xpEvent = new XpGainedEvent(operatorId, 1, 50, "Mission", createdEvent.Hash);
        var msg = CreateMessage(xpEvent);

        var result = OperatorEventReplicator.RehydrateEvent(msg);

        Assert.IsType<XpGainedEvent>(result);
        Assert.Equal(xpEvent.Hash, result.Hash);
    }

    [Fact]
    public void RehydrateEvent_UnknownType_ThrowsInvalidOperationException()
    {
        var msg = new OperatorEventBroadcastMessage
        {
            SenderId = Guid.NewGuid(),
            OperatorId = Guid.NewGuid(),
            SequenceNumber = 0,
            EventType = "UnknownEventType",
            Payload = "{}",
            PreviousHash = "",
            Hash = "abc",
            Timestamp = DateTimeOffset.UtcNow
        };

        Assert.Throws<InvalidOperationException>(() => OperatorEventReplicator.RehydrateEvent(msg));
    }

    // --- Helpers ---

    /// <summary>
    /// Polls <paramref name="store"/> until it contains at least <paramref name="expectedCount"/>
    /// events for <paramref name="operatorId"/>, or throws a <see cref="TimeoutException"/> after 1 s.
    /// </summary>
    private static async Task<IReadOnlyList<OperatorEvent>> WaitForEventsAsync(
        IOperatorEventStore store,
        OperatorId operatorId,
        int expectedCount,
        int timeoutMs = 1000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var events = await store.LoadEventsAsync(operatorId);
            if (events.Count >= expectedCount)
                return events;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Timed out waiting for {expectedCount} replicated operator event(s) " +
                    $"(got {events.Count}).");

            await Task.Delay(10);
        }
    }

    private static OperatorEventBroadcastMessage CreateMessage(OperatorEvent evt)
    {
        return new OperatorEventBroadcastMessage
        {
            SenderId = Guid.NewGuid(),
            OperatorId = evt.OperatorId.Value,
            SequenceNumber = evt.SequenceNumber,
            EventType = evt.EventType,
            Payload = evt.Payload,
            PreviousHash = evt.PreviousHash,
            Hash = evt.Hash,
            Timestamp = evt.Timestamp
        };
    }
}
