namespace GUNRPG.Api.Dtos;

public sealed class ApiOfflineMissionEnvelopeDto
{
    public string OperatorId { get; init; } = string.Empty;
    public long SequenceNumber { get; init; }
    public int RandomSeed { get; init; }
    public string InitialSnapshotJson { get; init; } = string.Empty;
    public string InitialOperatorStateHash { get; init; } = string.Empty;
    public string ResultOperatorStateHash { get; init; } = string.Empty;
    public List<ApiBattleLogEntryDto>? FullBattleLog { get; init; } = new();
    public DateTime ExecutedUtc { get; init; }
    public bool Synced { get; init; }
}
