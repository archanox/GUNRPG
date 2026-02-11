namespace GUNRPG.Api.Dtos;

/// <summary>
/// API-specific battle log entry DTO.
/// </summary>
public sealed class ApiBattleLogEntryDto
{
    public string EventType { get; init; } = "";
    public long TimeMs { get; init; }
    public string Message { get; init; } = "";
    public string? ActorName { get; init; }
}
