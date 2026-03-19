namespace GUNRPG.Security;

/// <summary>
/// A pending gameplay effect queued during a simulation tick and applied during <c>ApplyEffects</c>.
/// All fields are value-typed to preserve determinism.
/// </summary>
internal sealed class PendingEffect
{
    public const string DamagePlayer = "DamagePlayer";
    public const string HealPlayer = "HealPlayer";
    public const string DamageEnemy = "DamageEnemy";

    public string EffectType { get; }
    public int Amount { get; }
    public int TargetEnemyId { get; }
    public string Reason { get; }

    public PendingEffect(string effectType, int amount, int targetEnemyId = 0, string reason = "")
    {
        EffectType = effectType ?? throw new ArgumentNullException(nameof(effectType));
        Amount = amount;
        TargetEnemyId = targetEnemyId;
        Reason = reason ?? string.Empty;
    }
}
