using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Distributed;

/// <summary>
/// In-process pub/sub hub for real-time operator state change notifications.
/// <para>
/// The server publishes a notification each time an operator event is appended
/// (either locally via <see cref="Operators.OperatorExfilService"/> or via peer
/// replication in <see cref="OperatorEventReplicator"/>).
/// </para>
/// <para>
/// HTTP clients subscribe via the SSE endpoint and receive a notification
/// whenever the operator's state changes, allowing them to re-fetch and display
/// the updated state without polling or using libp2p.
/// </para>
/// </summary>
public sealed class OperatorUpdateHub
{
    private readonly ConcurrentDictionary<Guid, List<Channel<OperatorEvent>>> _subscriptions = new();
    private readonly object _subLock = new();

    /// <summary>
    /// Publishes an operator event to all active subscribers for that operator.
    /// Non-blocking; slow or disconnected subscribers are dropped.
    /// </summary>
    public void Publish(OperatorEvent evt)
    {
        var id = evt.OperatorId.Value;

        List<Channel<OperatorEvent>> snapshot;
        lock (_subLock)
        {
            if (!_subscriptions.TryGetValue(id, out var list))
                return;
            snapshot = [..list];
        }

        foreach (var channel in snapshot)
        {
            // TryWrite is non-blocking; drop if the channel buffer is full
            channel.Writer.TryWrite(evt);
        }
    }

    /// <summary>
    /// Subscribes to operator events for the given operator.
    /// The returned async enumerable yields events until <paramref name="ct"/> is cancelled.
    /// </summary>
    public async IAsyncEnumerable<OperatorEvent> SubscribeAsync(
        OperatorId operatorId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<OperatorEvent>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        var id = operatorId.Value;
        lock (_subLock)
        {
            if (!_subscriptions.TryGetValue(id, out var list))
            {
                list = [];
                _subscriptions[id] = list;
            }
            list.Add(channel);
        }

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                yield return evt;
            }
        }
        finally
        {
            Unsubscribe(id, channel);
        }
    }

    private void Unsubscribe(Guid operatorId, Channel<OperatorEvent> channel)
    {
        lock (_subLock)
        {
            if (_subscriptions.TryGetValue(operatorId, out var list))
            {
                list.Remove(channel);
                if (list.Count == 0)
                    _subscriptions.TryRemove(operatorId, out _);
            }
        }
        channel.Writer.TryComplete();
    }
}
