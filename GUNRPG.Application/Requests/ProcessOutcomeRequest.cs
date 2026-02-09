using GUNRPG.Core.Equipment;

namespace GUNRPG.Application.Requests;

public sealed class ProcessOutcomeRequest
{
    public Guid SessionId { get; init; }
    public bool OperatorDied { get; init; }
    public int XpGained { get; init; }
    public bool IsVictory { get; init; }
    public List<GearId> GearLost { get; init; } = new();
}
