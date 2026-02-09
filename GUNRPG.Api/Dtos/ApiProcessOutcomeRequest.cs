namespace GUNRPG.Api.Dtos;

public sealed class ApiProcessOutcomeRequest
{
    public Guid SessionId { get; init; }
    public bool OperatorDied { get; init; }
    public int XpGained { get; init; }
    public bool IsVictory { get; init; }
    public List<string> GearLost { get; init; } = new();
}
