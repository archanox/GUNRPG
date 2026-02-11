namespace GUNRPG.Application.Dtos;

/// <summary>
/// Represents a single battle log entry for display in the UI.
/// </summary>
public sealed class BattleLogEntryDto
{
    public string EventType { get; init; } = "";
    public long TimeMs { get; init; }
    public string Message { get; init; } = "";
    public string? ActorName { get; init; }
}
