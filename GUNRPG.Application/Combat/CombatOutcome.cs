using GUNRPG.Core.Equipment;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Combat;

/// <summary>
/// Represents the final, authoritative result of a completed combat session.
/// This is the only allowed handoff from combat (infil) to operator progression (exfil).
/// 
/// CombatOutcome is:
/// - Explicit: Contains everything Exfil needs
/// - Immutable: Properties are init-only
/// - Pure data: No domain behavior
/// - Free of service logic: Just a data contract
/// 
/// Combat logic must never mutate operator state. CombatOutcome must be producible
/// deterministically from a completed session.
/// </summary>
public sealed class CombatOutcome
{
    /// <summary>
    /// The combat session that produced this outcome.
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// The operator ID involved in combat.
    /// </summary>
    public OperatorId OperatorId { get; init; }

    /// <summary>
    /// Whether the operator died during combat.
    /// </summary>
    public bool OperatorDied { get; init; }

    /// <summary>
    /// Experience points gained during combat.
    /// </summary>
    public int XpGained { get; init; }

    /// <summary>
    /// Gear lost during combat (empty if none lost).
    /// </summary>
    public IReadOnlyCollection<GearId> GearLost { get; init; }

    /// <summary>
    /// Whether the combat was a victory (operator survived and enemy defeated).
    /// Optional metadata for context.
    /// </summary>
    public bool IsVictory { get; init; }

    /// <summary>
    /// Number of turns the operator survived.
    /// Optional metadata for context.
    /// </summary>
    public int TurnsSurvived { get; init; }

    /// <summary>
    /// Amount of damage taken during combat.
    /// Optional metadata for context.
    /// </summary>
    public float DamageTaken { get; init; }

    /// <summary>
    /// When the combat ended.
    /// Optional metadata for context.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }

    public CombatOutcome(
        Guid sessionId,
        OperatorId operatorId,
        bool operatorDied,
        int xpGained,
        IReadOnlyCollection<GearId> gearLost,
        bool isVictory = false,
        int turnsSurvived = 0,
        float damageTaken = 0f,
        DateTimeOffset? completedAt = null)
    {
        if (xpGained < 0)
            throw new ArgumentException("XP gained cannot be negative", nameof(xpGained));
        if (turnsSurvived < 0)
            throw new ArgumentException("Turns survived cannot be negative", nameof(turnsSurvived));
        if (damageTaken < 0)
            throw new ArgumentException("Damage taken cannot be negative", nameof(damageTaken));

        SessionId = sessionId;
        OperatorId = operatorId;
        OperatorDied = operatorDied;
        XpGained = xpGained;
        GearLost = gearLost ?? throw new ArgumentNullException(nameof(gearLost));
        IsVictory = isVictory;
        TurnsSurvived = turnsSurvived;
        DamageTaken = damageTaken;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
    }
}
