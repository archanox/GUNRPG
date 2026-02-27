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
