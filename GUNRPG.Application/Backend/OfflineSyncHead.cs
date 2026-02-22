namespace GUNRPG.Application.Backend;

public sealed class OfflineSyncHead
{
    public Guid OperatorId { get; init; }
    public long SequenceNumber { get; init; }
    public string ResultOperatorStateHash { get; init; } = string.Empty;
}
