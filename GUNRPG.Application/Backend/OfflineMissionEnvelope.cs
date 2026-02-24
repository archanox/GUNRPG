using GUNRPG.Application.Dtos;

namespace GUNRPG.Application.Backend;

public sealed class OfflineMissionEnvelope
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OperatorId { get; set; } = string.Empty;
    public long SequenceNumber { get; set; }
    public int RandomSeed { get; set; }

    /// <summary>Canonical JSON snapshot of operator state immediately before mission execution.</summary>
    public string InitialSnapshotJson { get; set; } = string.Empty;

    /// <summary>Canonical JSON snapshot of operator state immediately after mission execution.</summary>
    public string ResultSnapshotJson { get; set; } = string.Empty;

    public string InitialOperatorStateHash { get; set; } = string.Empty;
    public string ResultOperatorStateHash { get; set; } = string.Empty;

    /// <summary>Full deterministic battle log; sufficient to replay the mission from InitialSnapshotJson + RandomSeed.</summary>
    public List<BattleLogEntryDto> FullBattleLog { get; set; } = new();

    public DateTime ExecutedUtc { get; set; }
    public bool Synced { get; set; }
}
