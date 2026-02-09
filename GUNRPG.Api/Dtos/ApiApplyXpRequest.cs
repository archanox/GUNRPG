namespace GUNRPG.Api.Dtos;

public sealed class ApiApplyXpRequest
{
    public int XpAmount { get; init; }
    public string Reason { get; init; } = "";
}
