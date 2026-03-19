using GUNRPG.Application.Gameplay;

namespace GUNRPG.Security;

/// <summary>
/// Full deterministic simulation state at a given tick.
/// Holds all mutable state threaded through the simulation loop:
/// tick counter, player state, enemy list, seeded RNG, pending effects, and emitted events.
/// </summary>
internal sealed class SimulationState
{
    private readonly List<PendingEffect> _pendingEffects = [];
    private readonly List<GameplayLedgerEvent> _events = [];
    private List<SimulationEnemyState> _enemies;

    /// <summary>Current simulation tick (incremented at the end of each <c>AdvanceSimulation</c> call).</summary>
    public int Tick { get; private set; }

    /// <summary>Current player state (replaced immutably on each update).</summary>
    public SimulationPlayerState Player { get; private set; }

    /// <summary>Ordered list of enemy entities alive at this tick.</summary>
    public IReadOnlyList<SimulationEnemyState> Enemies => _enemies;

    /// <summary>Single seeded RNG instance — the only source of randomness in the simulation.</summary>
    public Random Rng { get; }

    /// <summary>Effects queued by the current tick, consumed during <c>ApplyEffects</c>.</summary>
    public IReadOnlyList<PendingEffect> PendingEffects => _pendingEffects;

    /// <summary>All gameplay events emitted so far across all ticks.</summary>
    public IReadOnlyList<GameplayLedgerEvent> Events => _events;

    public SimulationState(Random rng, SimulationPlayerState player, IEnumerable<SimulationEnemyState> enemies)
    {
        Rng = rng ?? throw new ArgumentNullException(nameof(rng));
        Player = player ?? throw new ArgumentNullException(nameof(player));
        _enemies = (enemies ?? throw new ArgumentNullException(nameof(enemies))).ToList();
    }

    internal void IncrementTick() => Tick++;
    internal void UpdatePlayer(SimulationPlayerState player) => Player = player;
    internal void UpdateEnemies(IReadOnlyList<SimulationEnemyState> enemies) => _enemies = [.. enemies];
    internal void AddPendingEffect(PendingEffect effect) => _pendingEffects.Add(effect);
    internal void ClearPendingEffects() => _pendingEffects.Clear();
    internal void EmitEvent(GameplayLedgerEvent evt) => _events.Add(evt);
}
