using GUNRPG.Application.Combat;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.AI;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.VirtualPet;
using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Weapons;

namespace GUNRPG.Application.Mapping;

public static class SessionMapping
{
    public static CombatSessionDto ToDto(CombatSession session)
    {
        var battleLog = BattleLogFormatter.FormatEvents(
            session.Combat.ExecutedEvents, 
            session.Player, 
            session.Enemy);

        return new CombatSessionDto
        {
            Id = session.Id,
            OperatorId = session.OperatorId.Value,
            Phase = session.Phase,
            CurrentTimeMs = session.Combat.CurrentTimeMs,
            Player = ToDto(session.Player),
            Enemy = ToDto(session.Enemy),
            Pet = ToDto(session.PetState),
            EnemyLevel = session.EnemyLevel,
            TurnNumber = session.TurnNumber,
            BalanceSnapshotVersion = session.BalanceSnapshotVersion,
            BalanceSnapshotHash = session.BalanceSnapshotHash,
            BattleLog = battleLog
        };
    }

    /// <summary>
    /// Creates a <see cref="CombatSessionDto"/> directly from a persisted snapshot.
    /// The resulting DTO has an empty <c>BattleLog</c> because <c>ExecutedEvents</c> is
    /// ephemeral state that is not persisted in the snapshot — this is consistent with
    /// <see cref="CombatSessionService.GetStateAsync"/>, which also reconstructs from a snapshot.
    /// Use this overload instead of <c>ToDto(session)</c> when you want the DTO to represent
    /// the stored (persisted) view of a session rather than an in-flight simulation view.
    /// </summary>
    public static CombatSessionDto ToDtoFromSnapshot(CombatSessionSnapshot snapshot)
    {
        return new CombatSessionDto
        {
            Id = snapshot.Id,
            OperatorId = snapshot.OperatorId,
            Phase = snapshot.Phase,
            CurrentTimeMs = snapshot.Combat.CurrentTimeMs,
            Player = ToDtoFromSnapshot(snapshot.Player, snapshot.BalanceSnapshotHash),
            Enemy = ToDtoFromSnapshot(snapshot.Enemy, snapshot.BalanceSnapshotHash),
            Pet = ToDtoFromSnapshot(snapshot.Pet),
            EnemyLevel = snapshot.EnemyLevel,
            TurnNumber = snapshot.TurnNumber,
            BalanceSnapshotVersion = snapshot.BalanceSnapshotVersion,
            BalanceSnapshotHash = snapshot.BalanceSnapshotHash,
            BattleLog = []
        };
    }

    public static PlayerStateDto ToDto(Operator op)
    {
        return new PlayerStateDto
        {
            Id = op.Id,
            Name = op.Name,
            Health = op.Health,
            MaxHealth = op.MaxHealth,
            Stamina = op.Stamina,
            Fatigue = op.Fatigue,
            SuppressionLevel = op.SuppressionLevel,
            IsSuppressed = op.IsSuppressed,
            DistanceToOpponent = op.DistanceToOpponent,
            CurrentAmmo = op.CurrentAmmo,
            MagazineSize = op.EquippedWeapon?.MagazineSize,
            AimState = op.AimState,
            MovementState = op.MovementState,
            CurrentMovement = op.CurrentMovement,
            CurrentDirection = op.CurrentDirection,
            CurrentCover = op.CurrentCover,
            IsMoving = op.IsMoving,
            IsAlive = op.IsAlive
        };
    }

    public static PetStateDto ToDto(PetState pet)
    {
        return new PetStateDto
        {
            Health = pet.Health,
            Fatigue = pet.Fatigue,
            Injury = pet.Injury,
            Stress = pet.Stress,
            Morale = pet.Morale,
            Hunger = pet.Hunger,
            Hydration = pet.Hydration,
            LastUpdated = pet.LastUpdated
        };
    }

    public static SimultaneousIntents ToDomainIntent(Guid operatorId, IntentDto dto)
    {
        return new SimultaneousIntents(operatorId)
        {
            Primary = dto.Primary ?? PrimaryAction.None,
            Movement = dto.Movement ?? MovementAction.Stand,
            Stance = dto.Stance ?? StanceAction.None,
            Cover = dto.Cover ?? CoverAction.None,
            CancelMovement = dto.CancelMovement
        };
    }

    public static CombatSessionSnapshot ToSnapshot(CombatSession session)
    {
        var (playerIntents, enemyIntents) = session.Combat.GetPendingIntents();
        var (randomSeed, randomCalls) = session.Combat.GetRandomState();

        return new CombatSessionSnapshot
        {
            Id = session.Id,
            OperatorId = session.OperatorId.Value,
            Phase = session.Phase,
            TurnNumber = session.TurnNumber,
            EnemyLevel = session.EnemyLevel,
            PlayerLevel = session.PlayerLevel,
            Seed = session.Seed,
            PostCombatResolved = session.PostCombatResolved,
            CreatedAt = session.CreatedAt,
            CompletedAt = session.CompletedAt,
            LastActionTimestamp = session.LastActionTimestamp,
            ReplayInitialSnapshotJson = session.ReplayInitialSnapshotJson,
            ReplayTurns = session.ReplayTurns.Select(ToSnapshot).ToList(),
            BalanceSnapshotVersion = session.BalanceSnapshotVersion,
            BalanceSnapshotHash = session.BalanceSnapshotHash,
            Version = session.Version,
            FinalHash = session.FinalHash != null ? (byte[])session.FinalHash.Clone() : null,
            Combat = new CombatStateSnapshot
            {
                Phase = session.Combat.Phase,
                CurrentTimeMs = session.Combat.CurrentTimeMs,
                PlayerIntents = ToSnapshot(playerIntents),
                EnemyIntents = ToSnapshot(enemyIntents),
                RandomState = new RandomStateSnapshot { Seed = randomSeed, CallCount = randomCalls }
            },
            Player = ToSnapshot(session.Player),
            Enemy = ToSnapshot(session.Enemy),
            Pet = ToSnapshot(session.PetState)
        };
    }

    public static CombatSession FromSnapshot(CombatSessionSnapshot snapshot)
    {
        var player = ToOperator(snapshot.Player, snapshot.BalanceSnapshotHash);
        var enemy = ToOperator(snapshot.Enemy, snapshot.BalanceSnapshotHash);

        var combat = CombatSystemV2.FromState(
            player,
            enemy,
            snapshot.Combat.Phase,
            snapshot.Combat.CurrentTimeMs,
            ToDomainIntent(snapshot.Combat.PlayerIntents),
            ToDomainIntent(snapshot.Combat.EnemyIntents),
            snapshot.Combat.RandomState.Seed,
            snapshot.Combat.RandomState.CallCount);

        var ai = new SimpleAIV2(seed: snapshot.Seed);
        var operatorManager = new OperatorManager();
        var petState = new PetState(
            snapshot.Pet.OperatorId,
            snapshot.Pet.Health,
            snapshot.Pet.Fatigue,
            snapshot.Pet.Injury,
            snapshot.Pet.Stress,
            snapshot.Pet.Morale,
            snapshot.Pet.Hunger,
            snapshot.Pet.Hydration,
            snapshot.Pet.LastUpdated);

        // Backwards compatibility: fallback to Player.Id if OperatorId is empty
        var operatorId = snapshot.OperatorId != Guid.Empty 
            ? OperatorId.FromGuid(snapshot.OperatorId)
            : OperatorId.FromGuid(snapshot.Player.Id);

        return new CombatSession(
            snapshot.Id,
            operatorId,
            combat,
            ai,
            operatorManager,
            petState,
            snapshot.EnemyLevel,
            snapshot.Seed,
            snapshot.Phase,
            snapshot.TurnNumber,
            snapshot.CreatedAt,
            snapshot.CompletedAt,
            snapshot.LastActionTimestamp ?? snapshot.CreatedAt,
            snapshot.ReplayInitialSnapshotJson,
            snapshot.ReplayTurns,
            snapshot.BalanceSnapshotVersion,
            snapshot.BalanceSnapshotHash,
            snapshot.Version,
            finalHash: snapshot.FinalHash,
            playerLevel: snapshot.PlayerLevel)
        {
            PostCombatResolved = snapshot.PostCombatResolved
        };
    }

    private static OperatorSnapshot ToSnapshot(Operator op)
    {
        return new OperatorSnapshot
        {
            Id = op.Id,
            Name = op.Name,
            Health = op.Health,
            MaxHealth = op.MaxHealth,
            Stamina = op.Stamina,
            MaxStamina = op.MaxStamina,
            Fatigue = op.Fatigue,
            MaxFatigue = op.MaxFatigue,
            MovementState = op.MovementState,
            AimState = op.AimState,
            WeaponState = op.WeaponState,
            CurrentMovement = op.CurrentMovement,
            CurrentCover = op.CurrentCover,
            CurrentDirection = op.CurrentDirection,
            MovementEndTimeMs = op.MovementEndTimeMs,
            EquippedWeaponName = op.EquippedWeapon?.Name,
            CurrentAmmo = op.CurrentAmmo,
            DistanceToOpponent = op.DistanceToOpponent,
            LastDamageTimeMs = op.LastDamageTimeMs,
            HealthRegenDelayMs = op.HealthRegenDelayMs,
            HealthRegenRate = op.HealthRegenRate,
            StaminaRegenRate = op.StaminaRegenRate,
            SprintStaminaDrainRate = op.SprintStaminaDrainRate,
            SlideStaminaCost = op.SlideStaminaCost,
            WalkSpeed = op.WalkSpeed,
            SprintSpeed = op.SprintSpeed,
            SlideDistance = op.SlideDistance,
            SlideDurationMs = op.SlideDurationMs,
            CurrentRecoilX = op.CurrentRecoilX,
            CurrentRecoilY = op.CurrentRecoilY,
            RecoilRecoveryStartMs = op.RecoilRecoveryStartMs,
            RecoilRecoveryRate = op.RecoilRecoveryRate,
            Accuracy = op.Accuracy,
            AccuracyProficiency = op.AccuracyProficiency,
            ResponseProficiency = op.ResponseProficiency,
            ShotsFiredCount = op.ShotsFiredCount,
            BulletsFiredSinceLastReaction = op.BulletsFiredSinceLastReaction,
            MetersMovedSinceLastReaction = op.MetersMovedSinceLastReaction,
            ADSTransitionStartMs = op.ADSTransitionStartMs,
            ADSTransitionDurationMs = op.ADSTransitionDurationMs,
            IsActivelyFiring = op.IsActivelyFiring,
            IsCoverTransitioning = op.IsCoverTransitioning,
            CoverTransitionFromState = op.CoverTransitionFromState,
            CoverTransitionToState = op.CoverTransitionToState,
            CoverTransitionStartMs = op.CoverTransitionStartMs,
            CoverTransitionEndMs = op.CoverTransitionEndMs,
            SuppressionLevel = op.SuppressionLevel,
            SuppressionDecayStartMs = op.SuppressionDecayStartMs,
            LastSuppressionApplicationMs = op.LastSuppressionApplicationMs,
            FlinchSeverity = op.FlinchSeverity,
            FlinchShotsRemaining = op.FlinchShotsRemaining,
            FlinchDurationShots = op.FlinchDurationShots,
            RecognitionDelayEndMs = op.RecognitionDelayEndMs,
            RecognitionTargetId = op.RecognitionTargetId,
            LastTargetVisibleMs = op.LastTargetVisibleMs,
            RecognitionStartMs = op.RecognitionStartMs,
            WasTargetVisible = op.WasTargetVisible
        };
    }

    private static Operator ToOperator(OperatorSnapshot snapshot, string? balanceSnapshotHash)
    {
        var op = new Operator(snapshot.Name, snapshot.Id)
        {
            Health = snapshot.Health,
            MaxHealth = snapshot.MaxHealth,
            Stamina = snapshot.Stamina,
            MaxStamina = snapshot.MaxStamina,
            Fatigue = snapshot.Fatigue,
            MaxFatigue = snapshot.MaxFatigue,
            MovementState = snapshot.MovementState,
            AimState = snapshot.AimState,
            WeaponState = snapshot.WeaponState,
            CurrentMovement = snapshot.CurrentMovement,
            CurrentCover = snapshot.CurrentCover,
            CurrentDirection = snapshot.CurrentDirection,
            MovementEndTimeMs = snapshot.MovementEndTimeMs,
            CurrentAmmo = snapshot.CurrentAmmo,
            DistanceToOpponent = snapshot.DistanceToOpponent,
            LastDamageTimeMs = snapshot.LastDamageTimeMs,
            HealthRegenDelayMs = snapshot.HealthRegenDelayMs,
            HealthRegenRate = snapshot.HealthRegenRate,
            StaminaRegenRate = snapshot.StaminaRegenRate,
            SprintStaminaDrainRate = snapshot.SprintStaminaDrainRate,
            SlideStaminaCost = snapshot.SlideStaminaCost,
            WalkSpeed = snapshot.WalkSpeed,
            SprintSpeed = snapshot.SprintSpeed,
            SlideDistance = snapshot.SlideDistance,
            SlideDurationMs = snapshot.SlideDurationMs,
            CurrentRecoilX = snapshot.CurrentRecoilX,
            CurrentRecoilY = snapshot.CurrentRecoilY,
            RecoilRecoveryStartMs = snapshot.RecoilRecoveryStartMs,
            RecoilRecoveryRate = snapshot.RecoilRecoveryRate,
            Accuracy = snapshot.Accuracy,
            AccuracyProficiency = snapshot.AccuracyProficiency,
            ResponseProficiency = snapshot.ResponseProficiency,
            BulletsFiredSinceLastReaction = snapshot.BulletsFiredSinceLastReaction,
            MetersMovedSinceLastReaction = snapshot.MetersMovedSinceLastReaction,
            ADSTransitionStartMs = snapshot.ADSTransitionStartMs,
            ADSTransitionDurationMs = snapshot.ADSTransitionDurationMs,
            IsActivelyFiring = snapshot.IsActivelyFiring,
            IsCoverTransitioning = snapshot.IsCoverTransitioning,
            CoverTransitionFromState = snapshot.CoverTransitionFromState,
            CoverTransitionToState = snapshot.CoverTransitionToState,
            CoverTransitionStartMs = snapshot.CoverTransitionStartMs,
            CoverTransitionEndMs = snapshot.CoverTransitionEndMs,
            RecognitionDelayEndMs = snapshot.RecognitionDelayEndMs,
            RecognitionTargetId = snapshot.RecognitionTargetId,
            LastTargetVisibleMs = snapshot.LastTargetVisibleMs,
            RecognitionStartMs = snapshot.RecognitionStartMs,
            WasTargetVisible = snapshot.WasTargetVisible
        };

        var weapon = CreateWeapon(snapshot.EquippedWeaponName, balanceSnapshotHash);
        if (weapon != null)
        {
            op.EquippedWeapon = weapon;
            op.CurrentAmmo = snapshot.CurrentAmmo;
        }

        op.RestoreSuppression(snapshot.SuppressionLevel, snapshot.LastSuppressionApplicationMs, snapshot.SuppressionDecayStartMs);
        op.RestoreFlinch(snapshot.FlinchSeverity, snapshot.FlinchShotsRemaining, snapshot.FlinchDurationShots);
        op.RestoreShotsFired(snapshot.ShotsFiredCount);
        return op;
    }

    private static IntentSnapshot? ToSnapshot(SimultaneousIntents? intents)
    {
        if (intents == null)
            return null;

        return new IntentSnapshot
        {
            OperatorId = intents.OperatorId,
            Primary = intents.Primary,
            Movement = intents.Movement,
            Stance = intents.Stance,
            Cover = intents.Cover,
            CancelMovement = intents.CancelMovement,
            SubmittedAtMs = intents.SubmittedAtMs
        };
    }

    private static IntentSnapshot ToSnapshot(IntentSnapshot snapshot)
    {
        return new IntentSnapshot
        {
            OperatorId = snapshot.OperatorId,
            Primary = snapshot.Primary,
            Movement = snapshot.Movement,
            Stance = snapshot.Stance,
            Cover = snapshot.Cover,
            CancelMovement = snapshot.CancelMovement,
            SubmittedAtMs = snapshot.SubmittedAtMs
        };
    }

    private static SimultaneousIntents? ToDomainIntent(IntentSnapshot? snapshot)
    {
        if (snapshot == null)
            return null;

        return new SimultaneousIntents(snapshot.OperatorId)
        {
            Primary = snapshot.Primary,
            Movement = snapshot.Movement,
            Stance = snapshot.Stance,
            Cover = snapshot.Cover,
            CancelMovement = snapshot.CancelMovement,
            SubmittedAtMs = snapshot.SubmittedAtMs
        };
    }

    private static PetStateSnapshot ToSnapshot(PetState pet)
    {
        return new PetStateSnapshot
        {
            OperatorId = pet.OperatorId,
            Health = pet.Health,
            Fatigue = pet.Fatigue,
            Injury = pet.Injury,
            Stress = pet.Stress,
            Morale = pet.Morale,
            Hunger = pet.Hunger,
            Hydration = pet.Hydration,
            LastUpdated = pet.LastUpdated
        };
    }

    private static PlayerStateDto ToDtoFromSnapshot(OperatorSnapshot op, string? balanceSnapshotHash)
    {
        var weapon = CreateWeapon(op.EquippedWeaponName, balanceSnapshotHash);
        return new PlayerStateDto
        {
            Id = op.Id,
            Name = op.Name,
            Health = op.Health,
            MaxHealth = op.MaxHealth,
            Stamina = op.Stamina,
            Fatigue = op.Fatigue,
            SuppressionLevel = op.SuppressionLevel,
            IsSuppressed = op.SuppressionLevel >= GUNRPG.Core.Combat.SuppressionModel.SuppressionThreshold,
            DistanceToOpponent = op.DistanceToOpponent,
            CurrentAmmo = op.CurrentAmmo,
            MagazineSize = weapon?.MagazineSize,
            AimState = op.AimState,
            MovementState = op.MovementState,
            CurrentMovement = op.CurrentMovement,
            CurrentDirection = op.CurrentDirection,
            CurrentCover = op.CurrentCover,
            IsMoving = op.MovementEndTimeMs.HasValue,
            IsAlive = op.Health > 0
        };
    }

    private static PetStateDto ToDtoFromSnapshot(PetStateSnapshot pet)
    {
        return new PetStateDto
        {
            Health = pet.Health,
            Fatigue = pet.Fatigue,
            Injury = pet.Injury,
            Stress = pet.Stress,
            Morale = pet.Morale,
            Hunger = pet.Hunger,
            Hydration = pet.Hydration,
            LastUpdated = pet.LastUpdated
        };
    }

    private static Weapon? CreateWeapon(string? name, string? balanceSnapshotHash)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var snapshotDescriptor = string.IsNullOrWhiteSpace(balanceSnapshotHash)
            ? $"current balance snapshot '{WeaponFactory.CurrentBalanceHash}'"
            : $"balance snapshot '{balanceSnapshotHash}'";

        return WeaponFactory.TryCreateWeapon(name, balanceSnapshotHash)
            ?? throw new InvalidOperationException($"Weapon '{name}' is not defined in {snapshotDescriptor}.");
    }
}
