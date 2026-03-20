namespace GUNRPG.Core.Simulation;

/// <summary>
/// Represents pure player intent for a replayable deterministic run.
/// Contains only player actions and the seed required to reproduce the simulation.
/// </summary>
public sealed record RunInput
{
    public Guid RunId { get; init; }

    public Guid PlayerId { get; init; }

    /// <summary>
    /// Seed used to initialize the deterministic RNG inside the simulation.
    /// Must be identical on every replay of this run to guarantee the same outcome.
    /// </summary>
    public int Seed { get; init; }

    public IReadOnlyList<PlayerAction> Actions { get; init; } = [];
}
