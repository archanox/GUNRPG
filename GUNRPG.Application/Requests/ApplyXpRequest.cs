namespace GUNRPG.Application.Requests;

public sealed class ApplyXpRequest
{
    public int XpAmount { get; init; }
    public string Reason { get; init; } = string.Empty;
}
