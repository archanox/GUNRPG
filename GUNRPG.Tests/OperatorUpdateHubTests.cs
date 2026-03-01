using GUNRPG.Application.Distributed;
using GUNRPG.Core.Operators;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for <see cref="OperatorUpdateHub"/>: verifying real-time push notifications
/// for operator state changes so connected clients (SSE subscribers) receive events
/// as they are appended without polling.
/// </summary>
public class OperatorUpdateHubTests
{
    // --- Publish / Subscribe ---

    [Fact]
    public async Task Publish_WithSubscriber_SubscriberReceivesEvent()
    {
        var hub = new OperatorUpdateHub();
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "Alpha");

        using var cts = new CancellationTokenSource();
        var received = new List<OperatorEvent>();

        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in hub.SubscribeAsync(operatorId, cts.Token))
                {
                    received.Add(e);
                    cts.Cancel(); // stop after first event
                }
            }
            catch (OperationCanceledException) { }
        });

        // Give the subscriber a moment to register
        await Task.Delay(10);

        hub.Publish(evt);

        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(received);
        Assert.Equal(evt.EventType, received[0].EventType);
        Assert.Equal(evt.OperatorId, received[0].OperatorId);
    }

    [Fact]
    public async Task Publish_NoSubscribers_DoesNotThrow()
    {
        var hub = new OperatorUpdateHub();
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "Bravo");

        // Should not throw even with no subscribers
        hub.Publish(evt);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Publish_MultipleSubscribers_AllReceiveEvent()
    {
        var hub = new OperatorUpdateHub();
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "Charlie");

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        var received1 = new List<OperatorEvent>();
        var received2 = new List<OperatorEvent>();

        var sub1 = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in hub.SubscribeAsync(operatorId, cts1.Token))
                {
                    received1.Add(e);
                    cts1.Cancel();
                }
            }
            catch (OperationCanceledException) { }
        });

        var sub2 = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in hub.SubscribeAsync(operatorId, cts2.Token))
                {
                    received2.Add(e);
                    cts2.Cancel();
                }
            }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(20); // let both subscribers register

        hub.Publish(evt);

        await Task.WhenAll(
            sub1.WaitAsync(TimeSpan.FromSeconds(2)),
            sub2.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.Single(received1);
        Assert.Single(received2);
    }

    [Fact]
    public async Task Publish_DifferentOperator_SubscriberDoesNotReceive()
    {
        var hub = new OperatorUpdateHub();
        var operatorIdA = OperatorId.NewId();
        var operatorIdB = OperatorId.NewId();
        var evtB = new OperatorCreatedEvent(operatorIdB, "Delta");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var received = new List<OperatorEvent>();

        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in hub.SubscribeAsync(operatorIdA, cts.Token))
                    received.Add(e);
            }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(10);
        hub.Publish(evtB); // publish for B, not A
        await subscribeTask;

        Assert.Empty(received);
    }

    [Fact]
    public async Task Subscribe_Cancelled_EndsCleanly()
    {
        var hub = new OperatorUpdateHub();
        var operatorId = OperatorId.NewId();

        using var cts = new CancellationTokenSource();

        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in hub.SubscribeAsync(operatorId, cts.Token)) { }
            }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(10);
        cts.Cancel();

        // Should complete without hanging
        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Publish_MultipleEvents_AllDeliveredInOrder()
    {
        var hub = new OperatorUpdateHub();
        var operatorId = OperatorId.NewId();
        var created = new OperatorCreatedEvent(operatorId, "Echo");
        var xp = new XpGainedEvent(operatorId, 1, 100, "Mission", created.Hash);

        using var cts = new CancellationTokenSource();
        var received = new List<OperatorEvent>();

        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in hub.SubscribeAsync(operatorId, cts.Token))
                {
                    received.Add(e);
                    if (received.Count >= 2) cts.Cancel();
                }
            }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(10);
        hub.Publish(created);
        hub.Publish(xp);

        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, received.Count);
        Assert.Equal("OperatorCreated", received[0].EventType);
        Assert.Equal("XpGained", received[1].EventType);
    }

    // --- Integration with OperatorEventReplicator ---

    [Fact]
    public async Task OperatorEventReplicator_WithHub_HubReceivesReplicatedEvent()
    {
        var nodeIdA = Guid.NewGuid();
        var nodeIdB = Guid.NewGuid();
        var transportA = new GUNRPG.Infrastructure.Distributed.InMemoryLockstepTransport(nodeIdA);
        var transportB = new GUNRPG.Infrastructure.Distributed.InMemoryLockstepTransport(nodeIdB);
        var storeA = new GUNRPG.Application.Operators.InMemoryOperatorEventStore();
        var storeB = new GUNRPG.Application.Operators.InMemoryOperatorEventStore();
        var hubB = new OperatorUpdateHub();

        var replicatorA = new OperatorEventReplicator(nodeIdA, transportA, storeA);
        var replicatorB = new OperatorEventReplicator(nodeIdB, transportB, storeB, hubB);
        transportA.ConnectTo(transportB);

        var operatorId = OperatorId.NewId();
        var created = new OperatorCreatedEvent(operatorId, "Foxtrot");

        using var cts = new CancellationTokenSource();
        var received = new List<OperatorEvent>();

        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in hubB.SubscribeAsync(operatorId, cts.Token))
                {
                    received.Add(e);
                    cts.Cancel();
                }
            }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(10);

        await storeA.AppendEventAsync(created);
        await replicatorA.BroadcastAsync(created);

        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(received);
        Assert.Equal("OperatorCreated", received[0].EventType);
    }
}
