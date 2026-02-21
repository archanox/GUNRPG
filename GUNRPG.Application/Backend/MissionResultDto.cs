namespace GUNRPG.Application.Backend;

/// <summary>
/// Result of a mission execution through the game backend.
/// </summary>
public sealed class MissionResultDto
{
    public string OperatorId { get; set; } = string.Empty;
    public bool Victory { get; set; }
    public long XpGained { get; set; }
    public string ResultJson { get; set; } = string.Empty;
}
