namespace GUNRPG.Core.Simulation;

public enum EffectKind
{
    DamagePlayer,
    HealPlayer,
    DamageEnemy
}

/// <summary>
/// A pending gameplay effect queued during a simulation tick and applied during deterministic event processing.
/// </summary>
public sealed class PendingEffect
{
    public EffectKind Kind { get; }
    public int Amount { get; }
    public int TargetEnemyId { get; }
    public string Reason { get; }

    public PendingEffect(EffectKind kind, int amount, int targetEnemyId = 0, string reason = "")
    {
        Kind = kind;
        Amount = amount;
        TargetEnemyId = targetEnemyId;
        Reason = reason ?? string.Empty;
    }
}
