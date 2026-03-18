namespace GUNRPG.Security;

/// <summary>
/// Represents pure player intent for a run.
/// Contains ONLY what the player did — no pre-computed mutations or events.
/// The replay engine derives all resulting events deterministically from Actions + Seed.
/// </summary>
public sealed record RunInput
{
    public Guid RunId { get; init; }

    public Guid PlayerId { get; init; }

    /// <summary>
    /// Seed used to initialize the deterministic RNG inside the replay engine.
    /// Must be identical on every replay of this run to guarantee the same outcome.
    /// </summary>
    public int Seed { get; init; }

    public IReadOnlyList<PlayerAction> Actions { get; init; } = [];
}
