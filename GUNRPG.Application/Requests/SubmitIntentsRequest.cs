using GUNRPG.Application.Dtos;

namespace GUNRPG.Application.Requests;

public sealed class SubmitIntentsRequest
{
    public IntentDto Intents { get; init; } = new();
    public bool AutoResolve { get; init; } = true;
}
