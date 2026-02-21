namespace GUNRPG.Application.Backend;

/// <summary>
/// Result of a mission execution through the game backend.
/// Structurally mirrors <see cref="GUNRPG.Application.Combat.CombatOutcome"/> fields.
/// 
/// TODO: Will be used when offline session-based combat is implemented.
/// Currently the interactive combat loop drives gameplay directly.
/// </summary>
public sealed class MissionResultDto
{
    public string OperatorId { get; set; } = string.Empty;
    public bool Victory { get; set; }
    public long XpGained { get; set; }
    public bool OperatorDied { get; set; }
    public int TurnsSurvived { get; set; }
    public float DamageTaken { get; set; }
    public string ResultJson { get; set; } = string.Empty;
}
