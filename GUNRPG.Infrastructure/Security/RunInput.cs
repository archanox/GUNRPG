namespace GUNRPG.Security;

public sealed record RunInput
{
    public Guid RunId { get; init; }

    public Guid PlayerId { get; init; }

    public IReadOnlyList<PlayerAction> Actions { get; init; } = [];
}
