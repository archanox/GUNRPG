namespace GUNRPG.Security;

/// <summary>
/// Immutable player state within the deterministic simulation.
/// Uses integer math exclusively — no floating-point arithmetic.
/// </summary>
internal sealed class SimulationPlayerState
{
    public int Health { get; }
    public int MaxHealth { get; }
    public bool IsAlive => Health > 0;

    public SimulationPlayerState(int health, int maxHealth)
    {
        Health = health;
        MaxHealth = maxHealth;
    }

    public SimulationPlayerState WithDamage(int amount) =>
        new(Math.Max(0, Health - amount), MaxHealth);

    public SimulationPlayerState WithHealing(int amount) =>
        new(Math.Min(MaxHealth, Health + amount), MaxHealth);
}
