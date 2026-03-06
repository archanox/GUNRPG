// Re-export shared types into the WebClient.Models namespace for backward compatibility.
global using OperatorSummary = GUNRPG.ClientModels.OperatorSummary;
global using OperatorState = GUNRPG.ClientModels.OperatorState;
global using StartInfilResponse = GUNRPG.ClientModels.StartInfilResponse;

namespace GUNRPG.WebClient.Models;

/// <summary>Request body for POST /operators.</summary>
public sealed class OperatorCreateRequest
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>Request body for POST /operators/{id}/loadout.</summary>
public sealed class ChangeLoadoutRequest
{
    public string WeaponName { get; set; } = string.Empty;
}

/// <summary>Request body for POST /operators/{id}/wounds/treat.</summary>
public sealed class TreatWoundsRequest
{
    public float HealthAmount { get; set; }
}

/// <summary>Request body for POST /operators/{id}/perks.</summary>
public sealed class UnlockPerkRequest
{
    public string PerkName { get; set; } = string.Empty;
}

/// <summary>Request body for POST /operators/{id}/pet.</summary>
public sealed class PetActionRequest
{
    public string Action { get; set; } = string.Empty;
    public float? Hours { get; set; }
    public float? Nutrition { get; set; }
    public float? Hydration { get; set; }
}
