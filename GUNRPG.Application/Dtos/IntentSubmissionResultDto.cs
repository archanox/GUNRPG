namespace GUNRPG.Application.Dtos;

public sealed class IntentSubmissionResultDto
{
    public bool Accepted { get; init; }
    public string? Error { get; init; }
    public CombatSessionDto? State { get; init; }
}
