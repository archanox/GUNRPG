namespace GUNRPG.Api.Dtos;

/// <summary>
/// API result for intent submission (decoupled from domain).
/// </summary>
public sealed class ApiIntentSubmissionResultDto
{
    public bool Accepted { get; init; }
    public string? Error { get; init; }
    public ApiCombatSessionDto? State { get; init; }
}
