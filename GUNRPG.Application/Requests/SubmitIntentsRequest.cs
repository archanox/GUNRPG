using GUNRPG.Application.Dtos;

namespace GUNRPG.Application.Requests;

/// <summary>
/// Request for submitting player intents (intent recording only, no auto-resolve).
/// </summary>
public sealed class SubmitIntentsRequest
{
    public IntentDto Intents { get; init; } = new();
    /// <summary>
    /// Optional caller operator ID. When provided, validated against the session's owning operator.
    /// </summary>
    public Guid? OperatorId { get; init; }
}
