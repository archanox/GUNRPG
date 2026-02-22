using GUNRPG.Application.Dtos;

namespace GUNRPG.Application.Backend;

public sealed class OfflineMissionEnvelope
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OperatorId { get; set; } = string.Empty;
    public long SequenceNumber { get; set; }
    public int RandomSeed { get; set; }
    public string InitialOperatorStateHash { get; set; } = string.Empty;
    public string ResultOperatorStateHash { get; set; } = string.Empty;
    public List<BattleLogEntryDto> FullBattleLog { get; set; } = new();
    public DateTime ExecutedUtc { get; set; }
    public bool Synced { get; set; }
}
