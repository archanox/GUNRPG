namespace GUNRPG.Application.Distributed;

/// <summary>
/// Abstraction over the P2P transport layer for lockstep replication.
/// Implementations may use libp2p, in-memory (for testing), or other transports.
/// </summary>
public interface ILockstepTransport
{
    /// <summary>Set of currently connected peer node IDs.</summary>
    IReadOnlySet<Guid> ConnectedPeers { get; }

    /// <summary>Broadcast an action to all connected peers.</summary>
    Task BroadcastActionAsync(ActionBroadcastMessage message, CancellationToken ct = default);

    /// <summary>Send an acknowledgment to a specific peer.</summary>
    Task SendAckAsync(Guid peerId, ActionAckMessage message, CancellationToken ct = default);

    /// <summary>Broadcast a state hash to all connected peers.</summary>
    Task BroadcastHashAsync(HashBroadcastMessage message, CancellationToken ct = default);

    /// <summary>Send a log sync request to a specific peer.</summary>
    Task SendSyncRequestAsync(Guid peerId, LogSyncRequestMessage message, CancellationToken ct = default);

    /// <summary>Send a log sync response to a specific peer.</summary>
    Task SendSyncResponseAsync(Guid peerId, LogSyncResponseMessage message, CancellationToken ct = default);

    /// <summary>Broadcast a single operator event to all connected peers.</summary>
    Task BroadcastOperatorEventAsync(OperatorEventBroadcastMessage message, CancellationToken ct = default);

    /// <summary>Send an operator event sync request to a specific peer.</summary>
    Task SendOperatorEventSyncRequestAsync(Guid peerId, OperatorEventSyncRequestMessage message, CancellationToken ct = default);

    /// <summary>Send an operator event sync response to a specific peer.</summary>
    Task SendOperatorEventSyncResponseAsync(Guid peerId, OperatorEventSyncResponseMessage message, CancellationToken ct = default);

    /// <summary>Raised when an action broadcast is received from a peer.</summary>
    event Action<ActionBroadcastMessage>? OnActionReceived;

    /// <summary>Raised when an action acknowledgment is received from a peer.</summary>
    event Action<ActionAckMessage>? OnAckReceived;

    /// <summary>Raised when a hash broadcast is received from a peer.</summary>
    event Action<HashBroadcastMessage>? OnHashReceived;

    /// <summary>Raised when a sync request is received from a peer.</summary>
    event Action<LogSyncRequestMessage>? OnSyncRequestReceived;

    /// <summary>Raised when a sync response is received from a peer.</summary>
    event Action<LogSyncResponseMessage>? OnSyncResponseReceived;

    /// <summary>Raised when a new peer connects.</summary>
    event Action<Guid>? OnPeerConnected;

    /// <summary>Raised when a peer disconnects.</summary>
    event Action<Guid>? OnPeerDisconnected;

    /// <summary>Raised when an operator event broadcast is received from a peer.</summary>
    event Action<OperatorEventBroadcastMessage>? OnOperatorEventReceived;

    /// <summary>Raised when an operator event sync request is received from a peer.</summary>
    event Action<OperatorEventSyncRequestMessage>? OnOperatorEventSyncRequestReceived;

    /// <summary>Raised when an operator event sync response is received from a peer.</summary>
    event Action<OperatorEventSyncResponseMessage>? OnOperatorEventSyncResponseReceived;
}
