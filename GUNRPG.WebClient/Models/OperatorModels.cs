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
