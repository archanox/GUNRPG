namespace GUNRPG.Security;

/// <summary>
/// Discriminates the type of a <see cref="PendingEffect"/>.
/// Using an enum instead of a string constant gives compile-time safety and avoids typo-at-runtime bugs.
/// </summary>
internal enum EffectKind
{
    DamagePlayer,
    HealPlayer,
    DamageEnemy
}

/// <summary>
/// A pending gameplay effect queued during a simulation tick and applied during <c>ApplyEffects</c>.
/// All fields are value-typed to preserve determinism.
/// </summary>
internal sealed class PendingEffect
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
