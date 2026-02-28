using GUNRPG.Application.Operators;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Distributed;

/// <summary>
/// Replicates operator events across all connected peers via the lockstep transport.
/// <para>
/// On peer connect: sends a sync request so the peer can share any operator events it knows about.
/// On sync request received: responds with all locally known operator events.
/// On operator event broadcast received: stores the event locally if not already present.
/// After local event append: broadcasts the event to all connected peers.
/// </para>
/// </summary>
public sealed class OperatorEventReplicator
{
    private readonly Guid _nodeId;
    private readonly ILockstepTransport _transport;
    private readonly IOperatorEventStore _eventStore;
    private readonly object _applyLock = new();

    public OperatorEventReplicator(Guid nodeId, ILockstepTransport transport, IOperatorEventStore eventStore)
    {
        _nodeId = nodeId;
        _transport = transport;
        _eventStore = eventStore;

        _transport.OnPeerConnected += OnPeerConnected;
        _transport.OnOperatorEventReceived += OnOperatorEventReceived;
        _transport.OnOperatorEventSyncRequestReceived += OnOperatorEventSyncRequestReceived;
        _transport.OnOperatorEventSyncResponseReceived += OnOperatorEventSyncResponseReceived;
    }

    /// <summary>
    /// Broadcasts a newly appended operator event to all connected peers.
    /// Call this after successfully appending an event to the local store.
    /// </summary>
    public async Task BroadcastAsync(OperatorEvent evt, CancellationToken ct = default)
    {
        if (_transport.ConnectedPeers.Count == 0) return;

        await _transport.BroadcastOperatorEventAsync(ToMessage(evt), ct);
    }

    // --- Transport event handlers ---

    private void OnPeerConnected(Guid peerId)
    {
        // Send a sync request so the new peer shares its known operator events
        _ = _transport.SendOperatorEventSyncRequestAsync(peerId, new OperatorEventSyncRequestMessage
        {
            SenderId = _nodeId
        });
    }

    private void OnOperatorEventReceived(OperatorEventBroadcastMessage msg)
    {
        _ = ApplyEventIfNewAsync(msg);
    }

    private void OnOperatorEventSyncRequestReceived(OperatorEventSyncRequestMessage msg)
    {
        _ = HandleSyncRequestAsync(msg);
    }

    private void OnOperatorEventSyncResponseReceived(OperatorEventSyncResponseMessage msg)
    {
        _ = HandleSyncResponseAsync(msg);
    }

    private async Task HandleSyncRequestAsync(OperatorEventSyncRequestMessage msg)
    {
        try
        {
            var allOperatorIds = await _eventStore.ListOperatorIdsAsync();
            var allEvents = new List<OperatorEventBroadcastMessage>();

            foreach (var opId in allOperatorIds)
            {
                var events = await _eventStore.LoadEventsAsync(opId);
                allEvents.AddRange(events.Select(ToMessage));
            }

            await _transport.SendOperatorEventSyncResponseAsync(msg.SenderId, new OperatorEventSyncResponseMessage
            {
                SenderId = _nodeId,
                Events = allEvents
            });
        }
        catch (Exception)
        {
            // Best-effort: ignore sync errors
        }
    }

    private async Task HandleSyncResponseAsync(OperatorEventSyncResponseMessage msg)
    {
        // Group by operator and sort by sequence to apply in order
        var grouped = msg.Events
            .GroupBy(e => e.OperatorId)
            .Select(g => g.OrderBy(e => e.SequenceNumber).ToList());

        foreach (var operatorEvents in grouped)
        {
            foreach (var evt in operatorEvents)
            {
                await ApplyEventIfNewAsync(evt);
            }
        }
    }

    private async Task ApplyEventIfNewAsync(OperatorEventBroadcastMessage msg)
    {
        try
        {
            var opId = OperatorId.FromGuid(msg.OperatorId);
            var currentSeq = await _eventStore.GetCurrentSequenceAsync(opId);

            // Skip events we already have
            if (msg.SequenceNumber <= currentSeq) return;

            // We can only apply the next event in sequence; skip if there's a gap
            if (msg.SequenceNumber != currentSeq + 1) return;

            var domainEvent = RehydrateEvent(msg);
            await _eventStore.AppendEventAsync(domainEvent);
        }
        catch (Exception)
        {
            // Best-effort: ignore replication errors (e.g. race conditions, hash mismatches)
        }
    }

    // --- Conversion helpers ---

    private static OperatorEventBroadcastMessage ToMessage(OperatorEvent evt)
    {
        return new OperatorEventBroadcastMessage
        {
            SenderId = Guid.Empty, // filled by caller/transport
            OperatorId = evt.OperatorId.Value,
            SequenceNumber = evt.SequenceNumber,
            EventType = evt.EventType,
            Payload = evt.Payload,
            PreviousHash = evt.PreviousHash,
            Hash = evt.Hash,
            Timestamp = evt.Timestamp
        };
    }

    /// <summary>
    /// Reconstructs the concrete <see cref="OperatorEvent"/> subtype from a wire message.
    /// Mirrors the mapping in <c>LiteDbOperatorEventStore.MapToDomainEvent</c>.
    /// </summary>
    public static OperatorEvent RehydrateEvent(OperatorEventBroadcastMessage msg)
    {
        var operatorId = OperatorId.FromGuid(msg.OperatorId);

        return msg.EventType switch
        {
            "OperatorCreated" => OperatorCreatedEvent.Rehydrate(operatorId, msg.Payload, msg.Timestamp),
            "XpGained" => XpGainedEvent.Rehydrate(operatorId, msg.SequenceNumber, msg.Payload, msg.PreviousHash, msg.Timestamp),
            "WoundsTreated" => WoundsTreatedEvent.Rehydrate(operatorId, msg.SequenceNumber, msg.Payload, msg.PreviousHash, msg.Timestamp),
            "LoadoutChanged" => LoadoutChangedEvent.Rehydrate(operatorId, msg.SequenceNumber, msg.Payload, msg.PreviousHash, msg.Timestamp),
            "PerkUnlocked" => PerkUnlockedEvent.Rehydrate(operatorId, msg.SequenceNumber, msg.Payload, msg.PreviousHash, msg.Timestamp),
            "CombatVictory" => CombatVictoryEvent.Rehydrate(operatorId, msg.SequenceNumber, msg.Payload, msg.PreviousHash, msg.Timestamp),
            "ExfilFailed" => ExfilFailedEvent.Rehydrate(operatorId, msg.SequenceNumber, msg.Payload, msg.PreviousHash, msg.Timestamp),
            "OperatorDied" => OperatorDiedEvent.Rehydrate(operatorId, msg.SequenceNumber, msg.Payload, msg.PreviousHash, msg.Timestamp),
            "InfilStarted" => InfilStartedEvent.Rehydrate(operatorId, msg.SequenceNumber, msg.Payload, msg.PreviousHash, msg.Timestamp),
            "InfilEnded" => InfilEndedEvent.Rehydrate(operatorId, msg.SequenceNumber, msg.Payload, msg.PreviousHash, msg.Timestamp),
            "CombatSessionStarted" => CombatSessionStartedEvent.Rehydrate(operatorId, msg.SequenceNumber, msg.Payload, msg.PreviousHash, msg.Timestamp),
            "PetActionApplied" => PetActionAppliedEvent.Rehydrate(operatorId, msg.SequenceNumber, msg.Payload, msg.PreviousHash, msg.Timestamp),
            _ => throw new InvalidOperationException($"Unknown event type for replication: {msg.EventType}")
        };
    }
}
