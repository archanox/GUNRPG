using GUNRPG.Core.Time;

namespace GUNRPG.Core.Simulation;

public sealed record RandomState(int Seed, int CallCount);

/// <summary>
/// Full deterministic simulation state at a given tick.
/// </summary>
public sealed class SimulationState
{
    public SimulationState(
        SimulationTime time,
        RandomState random,
        SimulationPlayerState player,
        IReadOnlyList<SimulationEnemyState> enemies,
        IReadOnlyList<SimulationEvent>? events = null,
        IReadOnlyList<SimulationEvent>? lastStepEvents = null)
    {
        Time = time ?? throw new ArgumentNullException(nameof(time));
        Random = random ?? throw new ArgumentNullException(nameof(random));
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
        Events = events ?? [];
        LastStepEvents = lastStepEvents ?? [];
    }

    public SimulationTime Time { get; }
    public RandomState Random { get; }
    public SimulationPlayerState Player { get; }
    public IReadOnlyList<SimulationEnemyState> Enemies { get; }
    public IReadOnlyList<SimulationEvent> Events { get; }
    public IReadOnlyList<SimulationEvent> LastStepEvents { get; }
}
