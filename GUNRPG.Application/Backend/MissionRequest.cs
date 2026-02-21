namespace GUNRPG.Application.Backend;

/// <summary>
/// Request to execute a mission through the game backend.
/// </summary>
public sealed class MissionRequest
{
    public string OperatorId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
}
