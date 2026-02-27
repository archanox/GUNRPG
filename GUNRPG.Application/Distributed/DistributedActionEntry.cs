namespace GUNRPG.Application.Distributed;

/// <summary>
/// An entry in the append-only distributed action log.
/// Each entry represents a deterministically applied action with ordering and hash verification.
/// </summary>
public sealed record DistributedActionEntry
{
    public required long SequenceNumber { get; init; }
    public required Guid NodeId { get; init; }
    public required PlayerActionDto Action { get; init; }
    public required string StateHashAfterApply { get; init; }
}
