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
        var subscriberReady = new TaskCompletionSource();

        var subscribeTask = StartSubscriberAsync(hub, operatorId, received, subscriberReady, cts);

        // Wait until the channel is registered before publishing
        await subscriberReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

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
        var ready1 = new TaskCompletionSource();
        var ready2 = new TaskCompletionSource();

        var sub1 = StartSubscriberAsync(hub, operatorId, received1, ready1, cts1);
        var sub2 = StartSubscriberAsync(hub, operatorId, received2, ready2, cts2);

        // Both must be registered before we publish
        await Task.WhenAll(
            ready1.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            ready2.Task.WaitAsync(TimeSpan.FromSeconds(2)));

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

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var received = new List<OperatorEvent>();
        var ready = new TaskCompletionSource();

        var subscribeTask = StartSubscriberAsync(hub, operatorIdA, received, ready, cts);

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));

        hub.Publish(evtB); // publish for B, not A
        await subscribeTask; // cancels via timeout CTS

        Assert.Empty(received);
    }

    [Fact]
    public async Task Subscribe_Cancelled_EndsCleanly()
    {
        var hub = new OperatorUpdateHub();
        var operatorId = OperatorId.NewId();

        using var cts = new CancellationTokenSource();
        var ready = new TaskCompletionSource();

        var subscribeTask = StartSubscriberAsync(hub, operatorId, new List<OperatorEvent>(), ready, cts);

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));
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
        var ready = new TaskCompletionSource();

        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await using var enumerator = hub.SubscribeAsync(operatorId, cts.Token)
                    .GetAsyncEnumerator(cts.Token);

                var pending = enumerator.MoveNextAsync();
                ready.TrySetResult(); // channel registered; safe to publish

                while (await pending)
                {
                    received.Add(enumerator.Current);
                    if (received.Count >= 2) cts.Cancel();
                    pending = enumerator.MoveNextAsync();
                }
            }
            catch (OperationCanceledException) { }
        });

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));
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
        var ready = new TaskCompletionSource();

        var subscribeTask = StartSubscriberAsync(hubB, operatorId, received, ready, cts);

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await storeA.AppendEventAsync(created);
        await replicatorA.BroadcastAsync(created);

        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(received);
        Assert.Equal("OperatorCreated", received[0].EventType);
    }

    // --- Helpers ---

    /// <summary>
    /// Starts a background subscriber task that collects received events into
    /// <paramref name="received"/> and signals <paramref name="subscriberReady"/> once
    /// the channel subscription is registered (guaranteed before any publish can reach it).
    /// Cancels <paramref name="cts"/> after the first event is received, then stops.
    /// </summary>
    private static Task StartSubscriberAsync(
        OperatorUpdateHub hub,
        OperatorId operatorId,
        List<OperatorEvent> received,
        TaskCompletionSource subscriberReady,
        CancellationTokenSource cts)
    {
        return Task.Run(async () =>
        {
            try
            {
                await using var enumerator = hub.SubscribeAsync(operatorId, cts.Token)
                    .GetAsyncEnumerator(cts.Token);

                // Calling MoveNextAsync executes SubscribeAsync up to the first await
                // (inside ReadAllAsync), where the channel is registered synchronously.
                // Signalling ready here guarantees the hub has the subscription before
                // the caller publishes.
                var pending = enumerator.MoveNextAsync();
                subscriberReady.TrySetResult();

                while (await pending)
                {
                    received.Add(enumerator.Current);
                    cts.Cancel(); // stop after first event
                    pending = enumerator.MoveNextAsync();
                }
            }
            catch (OperationCanceledException) { }
        });
    }
}
