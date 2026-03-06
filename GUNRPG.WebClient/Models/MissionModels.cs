// Re-export shared types into the WebClient.Models namespace for backward compatibility.
global using CombatSession = GUNRPG.ClientModels.CombatSession;
global using PlayerState = GUNRPG.ClientModels.PlayerState;
global using BattleLogEntry = GUNRPG.ClientModels.BattleLogEntry;

namespace GUNRPG.WebClient.Models;

/// <summary>Request body for POST /sessions (create a new combat session).</summary>
public sealed class SessionCreateRequest
{
    public Guid? Id { get; set; }
    public Guid? OperatorId { get; set; }
    public string? PlayerName { get; set; }
}

/// <summary>Request body for POST /sessions/{id}/intent.</summary>
public sealed class IntentRequest
{
    public IntentDto Intents { get; set; } = new();
    public Guid? OperatorId { get; set; }
}

/// <summary>Player intent choices for a combat turn.</summary>
public sealed class IntentDto
{
    public string? Primary { get; set; }
    public string? Movement { get; set; }
    public string? Stance { get; set; }
    public string? Cover { get; set; }
    public bool CancelMovement { get; set; }
}
