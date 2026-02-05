namespace GUNRPG.Api.Dtos;

/// <summary>
/// API request for submitting player intents (intent submission only, no auto-resolve).
/// </summary>
public sealed class ApiSubmitIntentsRequest
{
    public ApiIntentDto Intents { get; init; } = new();
}
