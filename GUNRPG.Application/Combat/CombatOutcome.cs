using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Combat;

/// <summary>
/// Represents the outcome of a combat session.
/// This is the boundary object that flows from infil (combat) to exfil (operator management).
/// Combat produces outcomes but does not persist operator changes directly.
/// </summary>
public sealed class CombatOutcome
{
    /// <summary>
    /// The combat session that produced this outcome.
    /// </summary>
    public Guid SessionId { get; }

    /// <summary>
    /// The operator ID involved in combat.
    /// </summary>
    public OperatorId OperatorId { get; }

    /// <summary>
    /// Whether the operator survived the combat.
    /// </summary>
    public bool Survived { get; }

    /// <summary>
    /// Amount of damage taken during combat.
    /// </summary>
    public float DamageTaken { get; }

    /// <summary>
    /// Amount of health remaining after combat.
    /// </summary>
    public float RemainingHealth { get; }

    /// <summary>
    /// Experience points earned during combat.
    /// </summary>
    public long XpEarned { get; }

    /// <summary>
    /// Reason for XP gain (e.g., "Victory", "Survived", "Kills").
    /// </summary>
    public string XpReason { get; }

    /// <summary>
    /// Number of enemies eliminated.
    /// </summary>
    public int EnemiesEliminated { get; }

    /// <summary>
    /// Whether the combat was a victory.
    /// </summary>
    public bool IsVictory { get; }

    /// <summary>
    /// When the combat ended.
    /// </summary>
    public DateTimeOffset CompletedAt { get; }

    public CombatOutcome(
        Guid sessionId,
        OperatorId operatorId,
        bool survived,
        float damageTaken,
        float remainingHealth,
        long xpEarned,
        string xpReason,
        int enemiesEliminated,
        bool isVictory,
        DateTimeOffset completedAt)
    {
        SessionId = sessionId;
        OperatorId = operatorId;
        Survived = survived;
        DamageTaken = damageTaken;
        RemainingHealth = remainingHealth;
        XpEarned = xpEarned;
        XpReason = xpReason ?? throw new ArgumentNullException(nameof(xpReason));
        EnemiesEliminated = enemiesEliminated;
        IsVictory = isVictory;
        CompletedAt = completedAt;
    }

    /// <summary>
    /// Creates a combat outcome from a completed combat session.
    /// </summary>
    public static CombatOutcome FromSession(Sessions.CombatSession session)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));

        var player = session.Player;
        var enemy = session.Enemy;

        var survived = player.IsAlive;
        var damageTaken = player.MaxHealth - player.Health;
        var remainingHealth = player.Health;

        // Calculate XP based on outcome
        var isVictory = player.IsAlive && !enemy.IsAlive;
        var enemiesEliminated = enemy.IsAlive ? 0 : 1;

        long xpEarned;
        string xpReason;

        if (isVictory)
        {
            xpEarned = 100; // Base XP for victory
            xpReason = "Victory";
        }
        else if (survived)
        {
            xpEarned = 50; // Partial XP for surviving
            xpReason = "Survived";
        }
        else
        {
            xpEarned = 10; // Minimal XP for participation
            xpReason = "Participation";
        }

        var operatorId = OperatorId.FromGuid(player.Id);

        return new CombatOutcome(
            session.Id,
            operatorId,
            survived,
            damageTaken,
            remainingHealth,
            xpEarned,
            xpReason,
            enemiesEliminated,
            isVictory,
            DateTimeOffset.UtcNow);
    }
}
