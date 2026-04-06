using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.VirtualPet;

namespace GUNRPG.Application.Sessions;

/// <summary>
/// Serializable snapshot capturing all persisted session state for storage.
/// </summary>
public sealed class CombatSessionSnapshot
{
    public Guid Id { get; init; }
    public Guid OperatorId { get; init; }  // ID of the operator in this session
    public SessionPhase Phase { get; init; }
    public int TurnNumber { get; init; }
    public CombatStateSnapshot Combat { get; init; } = default!;
    public OperatorSnapshot Player { get; init; } = default!;
    public OperatorSnapshot Enemy { get; init; } = default!;
    /// <summary>
    /// Persisted side-channel pet state. Replay carries this through snapshots but does not mutate
    /// or validate it; pet updates are applied after pure combat replay completes.
    /// </summary>
    public PetStateSnapshot Pet { get; init; } = default!;
    public int EnemyLevel { get; init; }
    /// <summary>
    /// Persisted level of the player operator at the time this session was created.
    /// Derived from the operator's total XP via <see cref="OpponentDifficulty.ComputeLevelFromXp"/>.
    /// Defaults to 0 for legacy sessions created before this field was introduced.
    /// When creating or cloning <see cref="CombatSessionSnapshot"/> instances always explicitly
    /// copy this field to avoid silently resetting difficulty calculations to level 0.
    /// </summary>
    public int PlayerLevel { get; init; }
    public int Seed { get; init; }
    public bool PostCombatResolved { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? LastActionTimestamp { get; init; }
    public string ReplayInitialSnapshotJson { get; init; } = string.Empty;
    public List<IntentSnapshot> ReplayTurns { get; init; } = new();
    public string BalanceSnapshotVersion { get; init; } = string.Empty;
    public string BalanceSnapshotHash { get; init; } = string.Empty;

    /// <summary>
    /// Schema version used when <see cref="FinalHash"/> was computed. Zero for legacy sessions.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Deterministic SHA-256 hash over replay-critical session data, set when the session
    /// transitions to <see cref="SessionPhase.Completed"/>. Null for in-progress sessions
    /// and legacy sessions loaded before this field was introduced.
    /// </summary>
    public byte[]? FinalHash { get; init; }
}

public sealed class CombatStateSnapshot
{
    public CombatPhase Phase { get; init; }
    public long CurrentTimeMs { get; init; }
    public IntentSnapshot? PlayerIntents { get; init; }
    public IntentSnapshot? EnemyIntents { get; init; }
    public RandomStateSnapshot RandomState { get; init; } = default!;
}

public sealed class RandomStateSnapshot
{
    public int Seed { get; init; }
    public int CallCount { get; init; }
}

public sealed class IntentSnapshot
{
    public Guid OperatorId { get; init; }
    public PrimaryAction Primary { get; init; }
    public MovementAction Movement { get; init; }
    public StanceAction Stance { get; init; }
    public CoverAction Cover { get; init; }
    public bool CancelMovement { get; init; }
    public long SubmittedAtMs { get; init; }
}

public sealed class OperatorSnapshot
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public float Health { get; init; }
    public float MaxHealth { get; init; }
    public float Stamina { get; init; }
    public float MaxStamina { get; init; }
    public float Fatigue { get; init; }
    public float MaxFatigue { get; init; }
    public MovementState MovementState { get; init; }
    public AimState AimState { get; init; }
    public WeaponState WeaponState { get; init; }
    public MovementState CurrentMovement { get; init; }
    public CoverState CurrentCover { get; init; }
    public MovementDirection CurrentDirection { get; init; }
    public long? MovementEndTimeMs { get; init; }
    public string? EquippedWeaponName { get; init; }
    public int CurrentAmmo { get; init; }
    public float DistanceToOpponent { get; init; }
    public long? LastDamageTimeMs { get; init; }
    public float HealthRegenDelayMs { get; init; }
    public float HealthRegenRate { get; init; }
    public float StaminaRegenRate { get; init; }
    public float SprintStaminaDrainRate { get; init; }
    public float SlideStaminaCost { get; init; }
    public float WalkSpeed { get; init; }
    public float SprintSpeed { get; init; }
    public float SlideDistance { get; init; }
    public float SlideDurationMs { get; init; }
    public float CurrentRecoilX { get; init; }
    public float CurrentRecoilY { get; init; }
    public long? RecoilRecoveryStartMs { get; init; }
    public float RecoilRecoveryRate { get; init; }
    public float Accuracy { get; init; }
    public float AccuracyProficiency { get; init; }
    public float ResponseProficiency { get; init; }
    public int ShotsFiredCount { get; init; }
    public int BulletsFiredSinceLastReaction { get; init; }
    public float MetersMovedSinceLastReaction { get; init; }
    public long? ADSTransitionStartMs { get; init; }
    public float ADSTransitionDurationMs { get; init; }
    public bool IsActivelyFiring { get; init; }
    public bool IsCoverTransitioning { get; init; }
    public CoverState CoverTransitionFromState { get; init; }
    public CoverState CoverTransitionToState { get; init; }
    public long? CoverTransitionStartMs { get; init; }
    public long? CoverTransitionEndMs { get; init; }
    public float SuppressionLevel { get; init; }
    public long? SuppressionDecayStartMs { get; init; }
    public long? LastSuppressionApplicationMs { get; init; }
    public float FlinchSeverity { get; init; }
    public int FlinchShotsRemaining { get; init; }
    public int FlinchDurationShots { get; init; }
    public long? RecognitionDelayEndMs { get; init; }
    public Guid? RecognitionTargetId { get; init; }
    public long? LastTargetVisibleMs { get; init; }
    public long? RecognitionStartMs { get; init; }
    public bool WasTargetVisible { get; init; }
}

public sealed class PetStateSnapshot
{
    public Guid OperatorId { get; init; }
    public float Health { get; init; }
    public float Fatigue { get; init; }
    public float Injury { get; init; }
    public float Stress { get; init; }
    public float Morale { get; init; }
    public float Hunger { get; init; }
    public float Hydration { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
}
