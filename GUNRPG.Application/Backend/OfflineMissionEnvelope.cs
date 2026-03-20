using GUNRPG.Application.Dtos;
using GUNRPG.Application.Sessions;

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

    /// <summary>Canonical JSON snapshot of the combat session before the first offline turn executes.</summary>
    public string InitialCombatSnapshotJson { get; set; } = string.Empty;

    /// <summary>Deterministic hash of the completed combat session snapshot after replaying all recorded turns.</summary>
    public string FinalCombatSnapshotHash { get; set; } = string.Empty;

    /// <summary>Ordered player turns that must be replayed against <see cref="InitialCombatSnapshotJson"/>.</summary>
    public List<IntentSnapshot> ReplayTurns { get; set; } = new();

    public string InitialOperatorStateHash { get; set; } = string.Empty;
    public string ResultOperatorStateHash { get; set; } = string.Empty;

    /// <summary>Full deterministic battle log; sufficient to replay the mission from InitialSnapshotJson + RandomSeed.</summary>
    public List<BattleLogEntryDto> FullBattleLog { get; set; } = new();

    public DateTime ExecutedUtc { get; set; }
    public bool Synced { get; set; }
}
