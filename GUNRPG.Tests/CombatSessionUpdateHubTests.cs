using GUNRPG.Application.Sessions;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for <see cref="CombatSessionUpdateHub"/>: verifying real-time push notifications
/// for combat session state changes so connected clients (SSE subscribers) receive events
/// as they are published without polling.
/// </summary>
public class CombatSessionUpdateHubTests
{
    [Fact]
    public async Task Publish_WithSubscriber_SubscriberReceivesNotification()
    {
        var hub = new CombatSessionUpdateHub();
        var sessionId = Guid.NewGuid();

        using var cts = new CancellationTokenSource();
        var received = new List<Guid>();
        var subscriberReady = new TaskCompletionSource();

        var subscribeTask = StartSubscriberAsync(hub, sessionId, received, subscriberReady, cts);

        await subscriberReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

        hub.Publish(sessionId);

        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(received);
        Assert.Equal(sessionId, received[0]);
    }

    [Fact]
    public async Task Publish_NoSubscribers_DoesNotThrow()
    {
        var hub = new CombatSessionUpdateHub();
        var sessionId = Guid.NewGuid();

        hub.Publish(sessionId);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Publish_MultipleSubscribers_AllReceiveNotification()
    {
        var hub = new CombatSessionUpdateHub();
        var sessionId = Guid.NewGuid();

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        var received1 = new List<Guid>();
        var received2 = new List<Guid>();
        var ready1 = new TaskCompletionSource();
        var ready2 = new TaskCompletionSource();

        var sub1 = StartSubscriberAsync(hub, sessionId, received1, ready1, cts1);
        var sub2 = StartSubscriberAsync(hub, sessionId, received2, ready2, cts2);

        await Task.WhenAll(
            ready1.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            ready2.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        hub.Publish(sessionId);

        await Task.WhenAll(
            sub1.WaitAsync(TimeSpan.FromSeconds(2)),
            sub2.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.Single(received1);
        Assert.Single(received2);
    }

    [Fact]
    public async Task Publish_DifferentSession_SubscriberDoesNotReceive()
    {
        var hub = new CombatSessionUpdateHub();
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var received = new List<Guid>();
        var ready = new TaskCompletionSource();

        var subscribeTask = StartSubscriberAsync(hub, sessionA, received, ready, cts);

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));

        hub.Publish(sessionB); // publish for B, not A
        await subscribeTask;   // cancels via timeout CTS

        Assert.Empty(received);
    }

    [Fact]
    public async Task Subscribe_Cancelled_EndsCleanly()
    {
        var hub = new CombatSessionUpdateHub();
        var sessionId = Guid.NewGuid();

        using var cts = new CancellationTokenSource();
        var ready = new TaskCompletionSource();

        var subscribeTask = StartSubscriberAsync(hub, sessionId, new List<Guid>(), ready, cts);

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();

        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Publish_MultipleEvents_AllDeliveredInOrder()
    {
        var hub = new CombatSessionUpdateHub();
        var sessionId = Guid.NewGuid();

        using var cts = new CancellationTokenSource();
        var received = new List<Guid>();
        var ready = new TaskCompletionSource();

        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await using var enumerator = hub.SubscribeAsync(sessionId, cts.Token)
                    .GetAsyncEnumerator(cts.Token);

                var pending = enumerator.MoveNextAsync();
                ready.TrySetResult();

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
        hub.Publish(sessionId);
        hub.Publish(sessionId);

        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, received.Count);
    }

    /// <summary>
    /// Starts a background subscriber task that collects received session IDs into
    /// <paramref name="received"/> and signals <paramref name="subscriberReady"/> once
    /// the channel subscription is registered. Cancels <paramref name="cts"/> after the
    /// first event is received, then stops.
    /// </summary>
    private static Task StartSubscriberAsync(
        CombatSessionUpdateHub hub,
        Guid sessionId,
        List<Guid> received,
        TaskCompletionSource subscriberReady,
        CancellationTokenSource cts)
    {
        return Task.Run(async () =>
        {
            try
            {
                await using var enumerator = hub.SubscribeAsync(sessionId, cts.Token)
                    .GetAsyncEnumerator(cts.Token);

                var pending = enumerator.MoveNextAsync();
                subscriberReady.TrySetResult();

                while (await pending)
                {
                    received.Add(enumerator.Current);
                    cts.Cancel();
                    pending = enumerator.MoveNextAsync();
                }
            }
            catch (OperationCanceledException) { }
        });
    }
}
