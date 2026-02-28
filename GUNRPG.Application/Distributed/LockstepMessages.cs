namespace GUNRPG.Application.Distributed;

/// <summary>
/// Protocol: /gunrpg/lockstep/1.0.0
/// All messages are serialized with System.Text.Json.
/// </summary>
public static class LockstepProtocol
{
    public const string Id = "/gunrpg/lockstep/1.0.0";
}

/// <summary>Broadcast a new action to all peers for consensus.</summary>
public sealed class ActionBroadcastMessage
{
    public required Guid SenderId { get; init; }
    public required long ProposedSequenceNumber { get; init; }
    public required PlayerActionDto Action { get; init; }
}

/// <summary>Acknowledge receipt of a broadcast action.</summary>
public sealed class ActionAckMessage
{
    public required Guid SenderId { get; init; }
    public required Guid AckedActionId { get; init; }
    public required long SequenceNumber { get; init; }
}

/// <summary>Broadcast the rolling state hash after applying an action.</summary>
public sealed class HashBroadcastMessage
{
    public required Guid SenderId { get; init; }
    public required long SequenceNumber { get; init; }
    public required string StateHash { get; init; }
}

/// <summary>Request missing log entries from a peer (reconnect sync).</summary>
public sealed class LogSyncRequestMessage
{
    public required Guid SenderId { get; init; }
    public required long FromSequenceNumber { get; init; }
    public required string LatestHash { get; init; }
}

/// <summary>Response with missing log entries for peer sync.</summary>
public sealed class LogSyncResponseMessage
{
    public required Guid SenderId { get; init; }
    public required List<DistributedActionEntry> Entries { get; init; }
    public required bool FullReplay { get; init; }
}

/// <summary>Broadcast a single operator event to all peers for replication.</summary>
public sealed class OperatorEventBroadcastMessage
{
    public required Guid SenderId { get; init; }
    public required Guid OperatorId { get; init; }
    public required long SequenceNumber { get; init; }
    public required string EventType { get; init; }
    public required string Payload { get; init; }
    public required string PreviousHash { get; init; }
    public required string Hash { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>Request all known operator events from a peer (initial sync).</summary>
public sealed class OperatorEventSyncRequestMessage
{
    public required Guid SenderId { get; init; }
}

/// <summary>Response with all known operator events for initial sync.</summary>
public sealed class OperatorEventSyncResponseMessage
{
    public required Guid SenderId { get; init; }
    public required List<OperatorEventBroadcastMessage> Events { get; init; }
}
