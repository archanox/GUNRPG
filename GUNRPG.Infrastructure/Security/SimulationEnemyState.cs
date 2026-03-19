namespace GUNRPG.Security;

/// <summary>
/// Immutable enemy entity state within the deterministic simulation.
/// Uses integer math exclusively — no floating-point arithmetic.
/// </summary>
internal sealed class SimulationEnemyState
{
    public int Id { get; }
    public int Health { get; }
    public int MaxHealth { get; }
    public bool IsAlive => Health > 0;

    public SimulationEnemyState(int id, int health, int maxHealth)
    {
        Id = id;
        Health = health;
        MaxHealth = maxHealth;
    }

    public SimulationEnemyState WithDamage(int amount) =>
        new(Id, Math.Max(0, Health - amount), MaxHealth);
}
