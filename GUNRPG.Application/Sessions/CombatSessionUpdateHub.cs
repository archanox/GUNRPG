using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace GUNRPG.Application.Sessions;

/// <summary>
/// In-process pub/sub hub for real-time combat session state change notifications.
/// <para>
/// The server publishes a notification each time a combat session is mutated
/// (intent submission, advance, pet action), allowing connected clients to
/// re-fetch the latest session state without polling.
/// </para>
/// <para>
/// HTTP clients subscribe via the SSE endpoint and receive a notification
/// whenever the session state changes, enabling cross-client real-time
/// synchronisation without requiring libp2p on the client.
/// </para>
/// </summary>
public sealed class CombatSessionUpdateHub
{
    private readonly Dictionary<Guid, List<Channel<Guid>>> _subscriptions = new();
    private readonly object _subLock = new();

    /// <summary>
    /// Publishes a notification to all active subscribers for the given session.
    /// Non-blocking; if a subscriber's channel buffer is full, the oldest buffered
    /// message is dropped to make room (see <see cref="BoundedChannelFullMode.DropOldest"/>).
    /// </summary>
    public void Publish(Guid sessionId)
    {
        List<Channel<Guid>> snapshot;
        lock (_subLock)
        {
            if (!_subscriptions.TryGetValue(sessionId, out var list))
                return;
            snapshot = [..list];
        }

        foreach (var channel in snapshot)
        {
            channel.Writer.TryWrite(sessionId);
        }
    }

    /// <summary>
    /// Subscribes to state-change notifications for the given combat session.
    /// The returned async enumerable yields the session ID on each change until
    /// <paramref name="ct"/> is cancelled.
    /// </summary>
    public async IAsyncEnumerable<Guid> SubscribeAsync(
        Guid sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        lock (_subLock)
        {
            if (!_subscriptions.TryGetValue(sessionId, out var list))
            {
                list = [];
                _subscriptions[sessionId] = list;
            }
            list.Add(channel);
        }

        try
        {
            await foreach (var id in channel.Reader.ReadAllAsync(ct))
            {
                yield return id;
            }
        }
        finally
        {
            Unsubscribe(sessionId, channel);
        }
    }

    private void Unsubscribe(Guid sessionId, Channel<Guid> channel)
    {
        lock (_subLock)
        {
            if (_subscriptions.TryGetValue(sessionId, out var list))
            {
                list.Remove(channel);
                if (list.Count == 0)
                    _subscriptions.Remove(sessionId);
            }
        }
        channel.Writer.TryComplete();
    }
}
